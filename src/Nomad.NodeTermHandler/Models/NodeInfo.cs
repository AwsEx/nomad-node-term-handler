namespace Nomad.NodeTermHandler.Models;

public class NodeInfo
{
    public string? AsgName { get; set; }
    public string? InstanceId { get; set; }
    public string? ProviderId { get; set; }
    public bool IsManaged { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    public string? PrivateIpAddress { get; set; }
}