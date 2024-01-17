using Amazon.AutoScaling;
using Amazon.EC2;
using Amazon.Runtime;
using Amazon.SQS;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Nomad.NodeTermHandler.Configuration;
using Nomad.NodeTermHandler.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Status = Nomad.NodeTermHandler.Models.Status;

namespace Nomad.NodeTermHandler;

public class Program
{
    public static int Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

            // This allows the ELB to override client ip and Request.Proto
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownProxies.Clear();
                options.KnownNetworks.Clear();
            });

            builder.Services.Configure<Settings>(builder.Configuration.GetSection("Settings"));

            builder.Services.AddControllers();

            builder.Services.AddEndpointsApiExplorer();

            builder.Services.AddHttpClient();

            builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions());
            builder.Services.AddSingleton(a => FallbackCredentialsFactory.GetCredentials());
            builder.Services.AddAWSService<IAmazonEC2>();
            builder.Services.AddAWSService<IAmazonAutoScaling>();
            builder.Services.AddAWSService<IAmazonSQS>();

            builder.Services.AddSingleton<ILogEventEnricher, SettingsEnricher>();

            builder.Services.AddSingleton<Store>();
            builder.Services.AddSingleton<Status, Status>();

            builder.Services.AddHostedService<NomadDrainingService>();

            builder.Services.AddOptions<HostOptions>()
                .Configure(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(60));

            builder.Host.UseSerilog((context, configuration) => configuration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithSettings()
            );

            var app = builder.Build();
            app.UseSerilogRequestLogging(o =>
            {
                o.EnrichDiagnosticContext = (dc, ctx) => dc.Set("UserAgent", ctx?.Request?.Headers["User-Agent"]);
                o.GetLevel = (ctx, ms, ex) => ex != null ? LogEventLevel.Error :
                    ctx.Response.StatusCode > 499 ? LogEventLevel.Error :
                    ms > 500 ? LogEventLevel.Warning :
                    ctx.Items.ContainsKey("Health") ? LogEventLevel.Debug : LogEventLevel.Information;
            });

            var status = app.Services.GetRequiredService<Status>();

            app.MapControllerRoute(
                "default",
                "{controller=Home}/{action=Index}/{id?}");

            // Configure the HTTP request pipeline.
            app.UseForwardedHeaders();

            if (app.Environment.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseHsts();
            }

            app.Lifetime.ApplicationStopped.Register(() => Log.Information("ApplicationStopped called"));

            app.Run();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
