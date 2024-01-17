namespace Nomad.NodeTermHandler.Configuration
{
    public class Settings
    {
        public int NodeTerminationGracePeriod { get; set; } = 60;
        public string? QueueName { get; set; } = "nomad-node-term";
        public string? ManagedTag { get; set; }
    }
}
