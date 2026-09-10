using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.SizeGates;

namespace Sekiban.Dcb.CosmosDb;

/// <summary>Provider-owned opt-in registration for Cosmos event-document size admission.</summary>
public static class CosmosExecutorSizeGateExtensions
{
    /// <summary>
    ///     Adds the Cosmos event-document policy to an explicit gate option set. The caller owns the supplied context
    ///     identity; the measurement certifies only a provider-created context, not an injected client.
    /// </summary>
    public static ExecutorSizeGateOptions AddCosmosEventDocumentPolicy(
        this ExecutorSizeGateOptions options,
        CosmosDbContext context,
        long? maxBytesPerEvent = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.Strict)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(context);

        var limit = maxBytesPerEvent ?? CosmosEventDocumentSizeMeasurement.DefaultMaxBytesPerEvent;
        ValidateLimit(limit, nameof(maxBytesPerEvent));

        return options.Add(new ExecutorSizePolicy(
            CosmosEventDocumentSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            maxBytesPerEvent: limit,
            strictness: strictness,
            measurement: new CosmosEventDocumentSizeMeasurement(context)));
    }

    /// <summary>
    ///     Registers the Cosmos event-document gate against the same lazy singleton context used by the standard
    ///     Cosmos event store registration.
    /// </summary>
    public static IServiceCollection AddSekibanDcbCosmosEventDocumentSizeGate(
        this IServiceCollection services,
        long? maxBytesPerEvent = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.Strict)
    {
        ArgumentNullException.ThrowIfNull(services);
        var limit = maxBytesPerEvent ?? CosmosEventDocumentSizeMeasurement.DefaultMaxBytesPerEvent;
        ValidateLimit(limit, nameof(maxBytesPerEvent));

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
                options =>
                {
                    var context = serviceProvider.GetService<CosmosDbContext>();
                    if (context is null)
                    {
                        options.Add(new ExecutorSizePolicy(
                            CosmosEventDocumentSizeMeasurement.Scope,
                            ExecutorSizeRepresentation.StorageItem,
                            maxBytesPerEvent: limit,
                            strictness: strictness,
                            measurement: new CosmosEventDocumentSizeMeasurement()));
                        return;
                    }

                    options.AddCosmosEventDocumentPolicy(context, limit, strictness);
                }));
        services.TryAddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<ExecutorSizeGateOptions>>().Value);

        return services;
    }

    private static void ValidateLimit(long limit, string parameterName)
    {
        if (limit <= 0 || limit > CosmosEventDocumentSizeMeasurement.DefaultMaxBytesPerEvent)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"Cosmos event-document limit must be between 1 and " +
                $"{CosmosEventDocumentSizeMeasurement.DefaultMaxBytesPerEvent} bytes.");
        }
    }
}
