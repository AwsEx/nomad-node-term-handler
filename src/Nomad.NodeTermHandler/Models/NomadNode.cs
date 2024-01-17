namespace Nomad.NodeTermHandler.Models;

public class NomadNode
{
    public string? Address { get; set; }
    public string? Id { get; set; }
    public bool Drain { get; set; }
    public string? Status { get; set; }
}