using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// Adds the largest-written-item policy. The helper exposes only the per-event quota for this scope.
    /// </summary>
    public static ExecutorSizeGateOptions AddDynamoDbMaxWrittenItemPolicy(
        this ExecutorSizeGateOptions options,
        DynamoDbEventStoreOptions? storeOptions = null,
        long? maxBytesPerEvent = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.Strict)
    {
        ArgumentNullException.ThrowIfNull(options);

        var eventLimit = maxBytesPerEvent ?? DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes;
        ValidateMaximumItemLimit(eventLimit, nameof(maxBytesPerEvent));

        return options.Add(new ExecutorSizePolicy(
            DynamoDbMaxWrittenItemSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            maxBytesPerEvent: eventLimit,
            strictness: strictness,
            measurement: new DynamoDbMaxWrittenItemSizeMeasurement(storeOptions)));
    }

    /// <summary>
    /// Adds the conservative whole-operation DynamoDB transaction-payload policy. The helper exposes only the
    /// operation quota for this scope.
    /// </summary>
    public static ExecutorSizeGateOptions AddDynamoDbWriteOperationPolicy(
        this ExecutorSizeGateOptions options,
        DynamoDbEventStoreOptions? storeOptions = null,
        long? maxBytesPerOperation = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.Strict)
    {
        ArgumentNullException.ThrowIfNull(options);

        var operationLimit = maxBytesPerOperation ?? DynamoDbWriteOperationSizeMeasurement.MaximumTransactionBytes;
        if (operationLimit <= 0 || operationLimit > DynamoDbWriteOperationSizeMeasurement.MaximumTransactionBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBytesPerOperation),
                $"DynamoDB write-operation limit must be between 1 and " +
                $"{DynamoDbWriteOperationSizeMeasurement.MaximumTransactionBytes} bytes.");
        }

        return options.Add(new ExecutorSizePolicy(
            DynamoDbWriteOperationSizeMeasurement.Scope,
            ExecutorSizeRepresentation.StorageItem,
            maxBytesPerOperation: operationLimit,
            strictness: strictness,
            measurement: new DynamoDbWriteOperationSizeMeasurement(storeOptions)));
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

        AddDynamoDbGateConfiguration(services, (options, serviceProvider) =>
        {
            var storeOptions = serviceProvider.GetService<IOptions<DynamoDbEventStoreOptions>>()?.Value;
            options.AddDynamoDbEventItemPolicy(
                storeOptions,
                maxBytesPerEvent,
                maxBytesPerOperation,
                strictness);
        });
        return services;
    }

    /// <summary>Registers the largest-written-item policy and preserves other provider size policies.</summary>
    public static IServiceCollection AddSekibanDcbDynamoDbMaxWrittenItemSizeGate(
        this IServiceCollection services,
        long? maxBytesPerEvent = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.Strict)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddDynamoDbGateConfiguration(services, (options, serviceProvider) =>
        {
            var storeOptions = serviceProvider.GetService<IOptions<DynamoDbEventStoreOptions>>()?.Value;
            options.AddDynamoDbMaxWrittenItemPolicy(storeOptions, maxBytesPerEvent, strictness);
        });
        return services;
    }

    /// <summary>Registers the conservative whole-operation transaction-payload policy.</summary>
    public static IServiceCollection AddSekibanDcbDynamoDbWriteOperationSizeGate(
        this IServiceCollection services,
        long? maxBytesPerOperation = null,
        ExecutorSizeStrictness strictness = ExecutorSizeStrictness.Strict)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddDynamoDbGateConfiguration(services, (options, serviceProvider) =>
        {
            var storeOptions = serviceProvider.GetService<IOptions<DynamoDbEventStoreOptions>>()?.Value;
            options.AddDynamoDbWriteOperationPolicy(storeOptions, maxBytesPerOperation, strictness);
        });
        return services;
    }

    private static void AddDynamoDbGateConfiguration(
        IServiceCollection services,
        Action<ExecutorSizeGateOptions, IServiceProvider> configure)
    {
        services.AddOptions<ExecutorSizeGateOptions>();
        services.AddSingleton<IConfigureOptions<ExecutorSizeGateOptions>>(serviceProvider =>
            new ConfigureNamedOptions<ExecutorSizeGateOptions>(
                Options.DefaultName,
                options => configure(options, serviceProvider)));
        services.TryAddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<ExecutorSizeGateOptions>>().Value);
    }

    private static void ValidateMaximumItemLimit(long limit, string parameterName)
    {
        if (limit <= 0 || limit > DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"DynamoDB maximum-written-item limit must be between 1 and " +
                $"{DynamoDbMaxWrittenItemSizeMeasurement.MaximumItemBytes} bytes.");
        }
    }
}
