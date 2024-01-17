namespace Nomad.NodeTermHandler.Models;

public class InterruptionEvent
{
    public string? EventId { get; set; }
    public string? Kind { get; set; }
    public string? Monitor { get; set; }
    public string? Description { get; set; }
    public string? State { get; set; }
    public string? AutoScalingGroupName { get; set; }
    public string? NodeName { get; set; }
    public Dictionary<string, string>? NodeLabels { get; set; }
    public string? InstanceId { get; set; }
    public string? ProviderId { get; set; }
    public bool IsManaged { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool NodeProcessed { get; set; }
    public bool InProgress { get; set; }
    public Func<Task>? PreDrainTask { get; set; }
    public Func<Task>? PostDrainTask { get; set; }
    public string? PrivateIpAddress { get; set; }

    public TimeSpan TimeUntilEvent()
    {
        return StartTime - DateTime.Now;
    }

    public bool IsRebalanceRecommendation()
    {
        return EventId?.Contains("rebalance-recommendation") ?? false;
    }
}

