using Serilog;
using Serilog.Configuration;

namespace Nomad.NodeTermHandler.Configuration;

public static class LoggingExtensions
{
    public static LoggerConfiguration WithSettings(
        this LoggerEnrichmentConfiguration enrich)
    {
        if (enrich == null)
            throw new ArgumentNullException(nameof(enrich));

        return enrich.With<SettingsEnricher>();
    }
}
