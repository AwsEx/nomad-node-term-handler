namespace Nomad.NodeTermHandler.Services;

public interface IMonitor
{
    Task Monitor(CancellationToken cancellationToken);
    string Kind();
}