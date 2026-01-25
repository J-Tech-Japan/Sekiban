using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Add services to the container.
public static class ServiceDefaultsExtensions
{
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Uncomment the following lines to enable the Azure Monitor exporter (requires the Azure.Monitor.OpenTelemetry.AspNetCore package)
        //if (!string.IsNullOrEmpty(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
        //{
        //    builder.Services.AddOpenTelemetry()
        //       .UseAzureMonitor();
        //}

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Health check endpoints are enabled for all environments to support:
        // - Azure Container Apps readiness/liveness probes
        // - Azure App Service health checks
        // - Load balancer health monitoring
        // Note: These endpoints only return health status, no sensitive data is exposed.

        // Readiness: All health checks must pass for app to be considered ready to accept traffic
        // Used by: ACA readiness probe, load balancer health checks
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions
        {
            // Include checks with "ready" tag, or checks without any tags
            Predicate = r => r.Tags.Contains("ready") || !r.Tags.Any()
        });

        // Liveness: Only basic checks to ensure app is responsive (not deadlocked)
        // Used by: ACA liveness probe, container orchestrator health monitoring
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        return app;
    }

    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";
}
