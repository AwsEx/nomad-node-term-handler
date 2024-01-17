using Newtonsoft.Json;

namespace Nomad.NodeTermHandler.Models;

public class AsgTerminationDetails
{
    [JsonProperty("EC2InstanceId")]
    public string? Ec2InstanceId { get; set; }
    [JsonProperty("AutoScalingGroupName")]
    public string? AutoScalingGroupName { get; set; }
    [JsonProperty("LifecycleHookName")]
    public string? LifecycleHookName { get; set; }
    [JsonProperty("LifecycleActionToken")]
    public string? LifecycleActionToken { get; set; }
}