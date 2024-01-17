using Amazon.AutoScaling;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Newtonsoft.Json;
using Nomad.NodeTermHandler.Models;
using Serilog;
using Tag = Amazon.EC2.Model.Tag;

namespace Nomad.NodeTermHandler.Services;

public class SqsMonitor : IMonitor
{
    public const string SqsMonitorKind = "SQS_MONITOR";
    public const string AsgTagName = "aws:autoscaling:groupName";

    private readonly IAmazonSQS _sqs;
    private readonly IAmazonEC2 _ec2Client;
    private readonly IAmazonAutoScaling _asg;
    private readonly string _queueUrl;
    private readonly string? _managedTag;
    private readonly bool _checkIfManaged;
    private readonly Action<InterruptionEvent>? _interruptionAction;
    private readonly Action<InterruptionEvent>? _cancelAction;
    public Action? BeforeCompleteLifecycleAction { get; set; }

    public SqsMonitor(
        IAmazonSQS sqs,
        IAmazonEC2 ec2Client,
        IAmazonAutoScaling asg,
        string queueUrl,
        string? managedTag,
        bool checkIfManaged,
        Action<InterruptionEvent> interruptionAction,
        Action<InterruptionEvent> cancelAction)
    {
        _sqs = sqs;
        _ec2Client = ec2Client;
        _asg = asg;
        _queueUrl = queueUrl;
        _managedTag = managedTag;
        _checkIfManaged = checkIfManaged;
        _interruptionAction = interruptionAction;
        _cancelAction = cancelAction;
    }

    public string Kind()
    {
        return SqsMonitorKind;
    }

    public async Task Monitor(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                Log.Logger.Debug("Checking for queue messages");
                var messages = await ReceiveQueueMessages(_queueUrl);

                int failedEventBridgeEvents = 0;
                foreach (var message in messages)
                {
                    try
                    {
                        var eventBridgeEvent = ProcessSQSMessage(message);
                        var interruptionEventWrappers = ProcessEventBridgeEvent(eventBridgeEvent, message);

                        try
                        {
                            await ProcessInterruptionEvents(interruptionEventWrappers, message);
                        }
                        catch (Exception ex)
                        {
                            Log.Logger.Error(ex, "Error processing interruption events");
                            failedEventBridgeEvents++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Logger.Warning(ex, "Error or skip processing SQS message");
                        failedEventBridgeEvents++;
                    }
                }

                if (messages.Any() && failedEventBridgeEvents == messages.Count)
                {
                    throw new Exception("None of the waiting queue events could be processed");
                }
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Error checking for queue messages");
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    public async Task ProcessInterruptionEvents(List<(InterruptionEvent? InterruptionEvent, Exception? Err)> interruptionEventWrappers, Message message)
    {
        int dropMessageSuggestionCount = 0;
        int failedInterruptionEventsCount = 0;

        foreach (var eventWrapper in interruptionEventWrappers)
        {
            if (eventWrapper.Err != null)
            {
                HandleEventError(eventWrapper.Err);
                failedInterruptionEventsCount++;
            }
            else if (eventWrapper.InterruptionEvent == null)
            {
                dropMessageSuggestionCount++;
            }
            else
            {
                HandleInterruptionEvent(eventWrapper.InterruptionEvent);
            }
        }

        if (dropMessageSuggestionCount == interruptionEventWrappers.Count)
        {
            await DeleteMessageAsync(message.ReceiptHandle);
        }

        if (failedInterruptionEventsCount > 0)
        {
            throw new Exception($"Some interruption events for message Id {message.MessageId} could not be processed");
        }
    }

    private void HandleEventError(Exception? eventWrapperErr)
    {
        Log.Logger.Error(eventWrapperErr, "Error processing interruption event");
    }

    private void HandleInterruptionEvent(InterruptionEvent evt)
    {
        _interruptionAction?.Invoke(evt);
    }

    public List<(InterruptionEvent? InterruptionEvent, Exception? Err)> ProcessEventBridgeEvent(EventBridgeEvent eventBridgeEvent, Message message)
    {
        var interruptionEventWrappers = new List<(InterruptionEvent? InterruptionEvent, Exception? Err)>();

        try
        {
            switch (eventBridgeEvent.Source)
            {
                case "aws.autoscaling":
                    // Process AutoScaling events
                    var asgInterruptionEvent = AsgTerminationToInterruptionEvent(eventBridgeEvent, message);
                    interruptionEventWrappers.Add((asgInterruptionEvent, null));
                    break;

                case "aws.ec2":

                    break;

                case "aws.health":
                    // Process AWS Health events
                    // Further handling based on eventBridgeEvent.DetailType
                    break;

                default:
                    throw new Exception($"Event source {eventBridgeEvent.Source} is not supported");
            }
        }
        catch (Exception? ex)
        {
            interruptionEventWrappers.Add((null, ex));
        }

        return interruptionEventWrappers;
    }

    public EventBridgeEvent ProcessSQSMessage(Message message)
    {
        try
        {
            return JsonConvert.DeserializeObject<EventBridgeEvent>(message.Body) ?? throw new Exception("Null after deserializing");
        }
        catch (JsonException ex)
        {
            throw new Exception("Error processing SQS message", ex);
        }
        catch (Exception ex)
        {
            throw new Exception("Error processing SQS message", ex);
        }
    }

    private async Task<List<Message>> ReceiveQueueMessages(string qUrl)
    {
        var receiveMessageRequest = new ReceiveMessageRequest
        {
            QueueUrl = qUrl,
            MaxNumberOfMessages = 10,
            VisibilityTimeout = 20, // 20 seconds
            WaitTimeSeconds = 20, // Max long polling
            AttributeNames = new List<string> { "SentTimestamp" },
            MessageAttributeNames = new List<string> { "All" }
        };

        var response = await _sqs.ReceiveMessageAsync(receiveMessageRequest);
        return response.Messages;
    }

    public async Task<InterruptionEvent?> Ec2StateChangeToInterruptionEvent(EventBridgeEvent eventBridgeEvent, Message message)
    {
        var ec2StateChangeDetail = JsonConvert.DeserializeObject<Ec2StateChangeDetail>(eventBridgeEvent.Detail) ?? throw new Exception("Error deserializing EC2 State Change Detail");

        var instanceStatesToDrain = new string[] { "stopping", "stopped", "shutting-down", "terminated" };
        if (!Array.Exists(instanceStatesToDrain, state => state.Equals(ec2StateChangeDetail.State, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var nodeInfo = await GetNodeInfo(ec2StateChangeDetail.InstanceId);
        if (nodeInfo == null)
        {
            throw new Exception($"Error retrieving node info for instance {ec2StateChangeDetail.InstanceId}");
        }

        var interruptionEvent = new InterruptionEvent
        {
            EventId = $"ec2-state-change-event-{eventBridgeEvent.Id}",
            Kind = "StateChange",
            Monitor = "SqsMonitor",
            StartTime = eventBridgeEvent.Time,
            NodeName = nodeInfo.Name,
            IsManaged = nodeInfo.IsManaged,
            AutoScalingGroupName = nodeInfo.AsgName,
            InstanceId = ec2StateChangeDetail.InstanceId,
            ProviderId = nodeInfo.ProviderId,
            Description = $"EC2 State Change event received. Instance {ec2StateChangeDetail.InstanceId} went into {ec2StateChangeDetail.State} at {eventBridgeEvent.Time}"
        };

        return interruptionEvent;
    }


    public InterruptionEvent? AsgTerminationToInterruptionEvent(EventBridgeEvent eventBridgeEvent, Message message)
    {
        var lifecycleDetail = JsonConvert.DeserializeObject<AsgTerminationDetails>(eventBridgeEvent.Detail);
        if (lifecycleDetail == null)
        {
            throw new Exception("Error deserializing ASG Termination Details");
        }

        var nodeInfo = GetNodeInfo(lifecycleDetail.Ec2InstanceId).Result;
        if (nodeInfo == null)
        {
            throw new Exception($"Error retrieving node info for instance {lifecycleDetail.Ec2InstanceId}");
        }

        var interruptionEvent = new InterruptionEvent
        {
            EventId = $"asg-termination-event-{eventBridgeEvent.Id}",
            Kind = "ASGTermination",
            Monitor = "SqsMonitor",
            StartTime = eventBridgeEvent.Time,
            NodeName = nodeInfo.Name,
            PrivateIpAddress = nodeInfo.PrivateIpAddress,
            IsManaged = nodeInfo.IsManaged,
            AutoScalingGroupName = nodeInfo.AsgName,
            InstanceId = lifecycleDetail.Ec2InstanceId,
            ProviderId = nodeInfo.ProviderId,
            Description = $"ASG Termination event received. Instance {lifecycleDetail.Ec2InstanceId} is terminating.",
        };

        interruptionEvent.PostDrainTask = async () =>
        {
            try
            {
                await _asg.CompleteLifecycleActionAsync(new Amazon.AutoScaling.Model.CompleteLifecycleActionRequest
                {
                    AutoScalingGroupName = lifecycleDetail.AutoScalingGroupName,
                    LifecycleActionResult = "CONTINUE",
                    LifecycleHookName = lifecycleDetail.LifecycleHookName,
                    LifecycleActionToken = lifecycleDetail.LifecycleActionToken,
                    InstanceId = lifecycleDetail.Ec2InstanceId
                });

                await DeleteMessageAsync(message.ReceiptHandle);
            }
            catch (Exception ex)
            {
                // Handle exceptions appropriately
                throw new Exception("Error in PostDrainTask", ex);
            }
        };

        return interruptionEvent;
    }

    private Task DeleteMessageAsync(string messageReceiptHandle)
    {
        return _sqs.DeleteMessageAsync(_queueUrl, messageReceiptHandle);
    }

    public async Task<Amazon.AutoScaling.Model.CompleteLifecycleActionResponse> CompleteLifecycleActionAsync(Amazon.AutoScaling.Model.CompleteLifecycleActionRequest request)
    {
        BeforeCompleteLifecycleAction?.Invoke();

        try
        {
            return await _asg.CompleteLifecycleActionAsync(request);
        }
        catch (Exception ex)
        {
            // Handle any exceptions that might occur during the API call
            throw new Exception("Error completing lifecycle action", ex);
        }
    }

    public async Task<NodeInfo> GetNodeInfo(string instanceId)
    {
        try
        {
            var request = new DescribeInstancesRequest
            {
                InstanceIds = new List<string> { instanceId }
            };

            var response = await _ec2Client.DescribeInstancesAsync(request);

            var reservation = response.Reservations.FirstOrDefault();
            if (reservation == null)
            {
                throw new Exception($"No reservations found for instance Id {instanceId}");
            }

            var instance = reservation.Instances.FirstOrDefault();
            if (instance == null)
            {
                throw new Exception($"No instance found with Id {instanceId}");
            }

            var nodeInfo = new NodeInfo
            {
                AsgName = FindTagValue(instance.Tags, AsgTagName),
                InstanceId = instance.InstanceId,
                ProviderId = instance.Placement?.AvailabilityZone != null ? $"aws:///{instance.Placement.AvailabilityZone}/{instance.InstanceId}" : "",
                IsManaged = CheckIfManaged(instance.Tags),
                Name = instance.PrivateDnsName,
                Tags = instance.Tags.ToDictionary(tag => tag.Key, tag => tag.Value),
                PrivateIpAddress = instance.PrivateIpAddress
            };

            return nodeInfo;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error retrieving node info for instance {instanceId}", ex);
        }
    }

    private string? FindTagValue(List<Tag> tags, string key)
    {
        var tag = tags.FirstOrDefault(t => t.Key == key);
        return tag?.Value;
    }

    private bool CheckIfManaged(List<Tag> tags)
    {
        // Implement logic to determine if the node is managed based on tags
        return true; // Placeholder logic
    }
}