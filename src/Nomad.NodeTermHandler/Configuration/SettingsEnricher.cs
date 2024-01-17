using Serilog.Core;
using Serilog.Events;

namespace Nomad.NodeTermHandler.Configuration;

public class SettingsEnricher : ILogEventEnricher
{
    private readonly Lazy<string> _version;

    public SettingsEnricher()
    {
        _version = new Lazy<string>(() => $"{GetType().Assembly.GetName().Version}");
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(new LogEventProperty("Application", new ScalarValue(Context.Application)));
        logEvent.AddPropertyIfAbsent(new LogEventProperty("Version", new ScalarValue(_version.Value)));
    }
}
