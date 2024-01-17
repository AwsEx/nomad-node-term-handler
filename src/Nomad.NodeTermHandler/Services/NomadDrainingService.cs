using Amazon.AutoScaling;
using Amazon.EC2;
using Amazon.SQS;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Nomad.NodeTermHandler.Configuration;
using Nomad.NodeTermHandler.Models;
using Serilog;
using static SimpleExec.Command;

namespace Nomad.NodeTermHandler.Services;

public class NomadDrainingService : BackgroundService
{
    public NomadDrainingService(IAmazonSQS sqs, IAmazonEC2 ec2, IAmazonAutoScaling asg, IOptions<Settings> config, Store store)
    {
        _sqs = sqs;
        _ec2 = ec2;
        _asg = asg;
        _config = config;
        _store = store;
    }

    private readonly IAmazonSQS _sqs;
    private readonly IAmazonEC2 _ec2;
    private readonly IAmazonAutoScaling _asg;
    private readonly IOptions<Settings> _config;
    private readonly Store _store;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var queueUrlResponse = await _sqs.GetQueueUrlAsync(_config.Value.QueueName, stoppingToken);


            var monitor = new SqsMonitor(_sqs, _ec2, _asg, queueUrlResponse.QueueUrl, _config.Value.ManagedTag, true,
                e => _store.AddInterruptionEvent(e), Cancel);
            var mt = monitor.Monitor(stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                var evt = _store.GetActiveEvent();
                if (evt.Item1 == null)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                evt.Item1.InProgress = true;
                await Drain(evt.Item1);
            }

            await mt;
        }
        catch (Amazon.SQS.Model.QueueDoesNotExistException e)
        {
            Log.Fatal(e, "SQS queue named {QueueName} not found!", _config.Value.QueueName);
            throw;
        }
    }

    private async Task Drain(InterruptionEvent evt)
    {
        evt.PreDrainTask?.Invoke();
            
        if (!string.IsNullOrEmpty(evt.PrivateIpAddress))
        {
            Log.Information("Draining {@evt}", evt);
            try
            {
                var nodes = await GetNomadNodes();
                var selected = nodes.FirstOrDefault(n => n.Address == evt.PrivateIpAddress);
                if (selected?.Id != null)
                {
                    if (selected.Status == "ready")
                        await RunAsync("nomad", $"node drain -enable {selected.Id}");
                    else
                        Log.Warning("Node {Id} is not ready (is {status}), skipping drain", selected.Id, selected.Status);
                }
                else
                    Log.Warning("Could not find node with private ip {PrivateIpAddress}", evt.PrivateIpAddress);
            }
            catch (Exception e)
            {
                Log.Error(e, "Error draining node");
            }
        }
        else
        {
            Log.Warning("PrivateIpAddress is null or empty, skipping drain");
        }

        evt.PostDrainTask?.Invoke();
        evt.NodeProcessed = true;
    }

    private static async Task<List<NomadNode>> GetNomadNodes()
    {
        var (standardOutput, _) = await ReadAsync("nomad", "node status -json");
        return JsonConvert.DeserializeObject<List<NomadNode>>(standardOutput) ?? throw new Exception("No nomad nodes found");
    }

    private void Cancel(InterruptionEvent evt)
    {
        Log.Information("Cancel drain {@evt}", evt);
    }
}