using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.SizeGates;

namespace Sekiban.Dcb.Orleans.AzureQueue;

/// <summary>Opt-in Azure Queue V2 destination-message size admission.</summary>
public static class OrleansAzureQueueExecutorSizeGateExtensions
{
    /// <summary>
    /// Adds the Azure Queue V2 destination-message policy to an existing executor gate option set.
    /// </summary>
    public static ExecutorSizeGateOptions AddOrleansAzureQueueStreamMessagePolicy(
        this ExecutorSizeGateOptions options,
        string streamProviderName,
        long? maxBytesPerEvent = null,
        long? maxBytesPerOperation = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.NonStrict)
    {
        ArgumentNullException.ThrowIfNull(options);
        OrleansAzureQueueStreamMessageSizeMeasurement.ValidateProviderName(streamProviderName);

        var eventLimit = maxBytesPerEvent ?? OrleansAzureQueueStreamMessageSizeMeasurement.DefaultMaxBytesPerEvent;
        if (eventLimit <= 0 || eventLimit > OrleansAzureQueueStreamMessageSizeMeasurement.DefaultMaxBytesPerEvent)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBytesPerEvent),
                $"Azure Queue V2 per-event limit must be between 1 and " +
                $"{OrleansAzureQueueStreamMessageSizeMeasurement.DefaultMaxBytesPerEvent} bytes.");
        }

        if (maxBytesPerOperation is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytesPerOperation));
        }

        return options.Add(new ExecutorSizePolicy(
            OrleansAzureQueueStreamMessageSizeMeasurement.Scope,
            ExecutorSizeRepresentation.Destination,
            eventLimit,
            maxBytesPerOperation,
            strictness,
            new OrleansAzureQueueStreamMessageSizeMeasurement(streamProviderName)));
    }

    /// <summary>
    /// Registers the Azure Queue V2 policy and its provider-owned capture bridge. The default is NonStrict so hosts
    /// can adopt the diagnostic before promoting an eligible named provider to Strict.
    /// </summary>
    public static IServiceCollection AddSekibanDcbOrleansAzureQueueStreamMessageSizeGate(
        this IServiceCollection services,
        string streamProviderName,
        long? maxBytesPerEvent = null,
        long? maxBytesPerOperation = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.NonStrict)
    {
        ArgumentNullException.ThrowIfNull(services);
        OrleansAzureQueueStreamMessageSizeMeasurement.ValidateProviderName(streamProviderName);

        // Validate before touching the service collection. This keeps an invalid provider registration from leaving a
        // mechanism marker which would make a later valid Core registration fail for an unrelated reason.
        var validatedOptions = new ExecutorSizeGateOptions();
        validatedOptions.AddOrleansAzureQueueStreamMessagePolicy(
            streamProviderName,
            maxBytesPerEvent,
            maxBytesPerOperation,
            strictness);

        ExecutorSizeGateRegistrationMarker.EnsureCompatible(
            services,
            ExecutorSizeGateRegistrationMarker.ProviderComposable);
        ExecutorSizeGateRegistrationMarker.Declare(
            services,
            ExecutorSizeGateRegistrationMarker.ProviderComposable);

        services.AddOptions<ExecutorSizeGateOptions>();
        services.AddSingleton<IConfigureOptions<ExecutorSizeGateOptions>>(serviceProvider =>
            new ConfigureNamedOptions<ExecutorSizeGateOptions>(
                Options.DefaultName,
                options => options.AddOrleansAzureQueueStreamMessagePolicy(
                    streamProviderName,
                    maxBytesPerEvent,
                    maxBytesPerOperation,
                    strictness)));
        services.TryAddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<ExecutorSizeGateOptions>>().Value);
        services.AddSingleton<IOrleansDestinationMeasurementCapture>(serviceProvider =>
            new OrleansAzureQueueDestinationMeasurementCapture(serviceProvider, streamProviderName));
        return services;
    }
}
