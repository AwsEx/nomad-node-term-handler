namespace Nomad.NodeTermHandler.Models
{
    public class Status
    {
        public bool ShuttingDown { get; private set; }

        public bool ApplicationIsStopping() => ShuttingDown = true;
    }
}
