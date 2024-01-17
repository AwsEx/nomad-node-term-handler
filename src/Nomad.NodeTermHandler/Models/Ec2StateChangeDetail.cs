using Newtonsoft.Json;

namespace Nomad.NodeTermHandler.Models;

public class Ec2StateChangeDetail
{
    [JsonProperty("InstanceID")]
    public string? InstanceId { get; set; }
    [JsonProperty("State")]
    public string? State { get; set; }
}