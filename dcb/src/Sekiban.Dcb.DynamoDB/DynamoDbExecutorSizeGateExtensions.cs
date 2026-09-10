using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.SizeGates;

namespace Sekiban.Dcb.DynamoDB;

/// <summary>Provider-owned opt-in registration for DynamoDB event-item size measurement.</summary>
public static class DynamoDbExecutorSizeGateExtensions
{
    /// <summary>
    /// Adds the canonical DynamoDB base event-item policy to an existing gate option set.
    /// The per-event default is the DynamoDB 400 KiB ceiling; a smaller quota is allowed.
    /// </summary>
    public static ExecutorSizeGateOptions AddDynamoDbEventItemPolicy(
        this ExecutorSizeGateOptions options,
        DynamoDbEventStoreOptions? storeOptions = null,
        long? maxBytesPerEvent = null,
        long? maxBytesPerOperation = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.Strict)
    {
        ArgumentNullException.ThrowIfNull(options);

        var eventLimit = maxBytesPerEvent ?? DynamoDbEventItemSizeMeasurement.MaximumItemBytes;
        if (eventLimit <= 0 || eventLimit > DynamoDbEventItemSizeMeasurement.MaximumItemBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBytesPerEvent),
                $"DynamoDB event-item per-event limit must be between 1 and " +
                $"{DynamoDbEventItemSizeMeasurement.MaximumItemBytes} bytes.");
        }

        if (maxBytesPerOperation is <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytesPerOperation));

        return options.Add(new ExecutorSizePolicy(
            DynamoDbEventItemSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            eventLimit,
            maxBytesPerOperation,
            strictness,
            new DynamoDbEventItemSizeMeasurement(storeOptions)));
    }

    /// <summary>
    /// Registers a DynamoDB event-item gate that resolves the provider's configured shard mapping from DI.
    /// The method is additive; without this call existing DynamoDB registration remains ungated.
    /// </summary>
    public static IServiceCollection AddSekibanDcbDynamoDbEventItemSizeGate(
        this IServiceCollection services,
        long? maxBytesPerEvent = null,
        long? maxBytesPerOperation = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.Strict)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<ExecutorSizeGateOptions>(serviceProvider =>
        {
            var storeOptions = serviceProvider.GetService<IOptions<DynamoDbEventStoreOptions>>()?.Value;
            return new ExecutorSizeGateOptions().AddDynamoDbEventItemPolicy(
                storeOptions,
                maxBytesPerEvent,
                maxBytesPerOperation,
                strictness);
        });
        return services;
    }
}
