using System.Text;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Providers.Streams.AzureQueue;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans;
using Sekiban.Dcb.SizeGates;

namespace Sekiban.Dcb.Orleans.AzureQueue;

/// <summary>
/// Measures the text produced by the actual Orleans Azure Queue V2 adapter and returns the conservative SDK envelope
/// bound. No Azure service or queue is contacted during measurement.
/// </summary>
public sealed class OrleansAzureQueueStreamMessageSizeMeasurement : IExecutorSizeMeasurement
{
    public const string Scope = "orleans-stream-message";
    public const long DefaultMaxBytesPerEvent = 65_536;

    private readonly string _streamProviderName;

    public OrleansAzureQueueStreamMessageSizeMeasurement(string streamProviderName)
    {
        _streamProviderName = ValidateProviderName(streamProviderName);
    }

    public ExecutorSizeMeasurementResult Measure(ExecutorSizeMeasurementContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Representation != ExecutorSizeRepresentation.Destination)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                "Orleans Azure Queue V2 measurement supports only the Destination representation");
        }

        if (context.DestinationPlan?.PreparedEvent is not { } preparedEvent ||
            preparedEvent.Id != context.SerializedEvent.Id)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                "the destination plan does not carry the admitted serialized event");
        }

        if (context.DestinationPlan?.ProviderState is not IReadOnlyList<OrleansDestinationPlanState> destinations)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                $"provider '{_streamProviderName}' did not provide an Orleans destination capture state");
        }

        var destination = destinations.FirstOrDefault(item =>
            string.Equals(item.DestinationKey, context.DestinationKey, StringComparison.Ordinal));
        if (destination is null)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                $"provider '{_streamProviderName}' did not provide destination '{context.DestinationKey}'");
        }

        if (destination.FailureReason is not null)
        {
            return ExecutorSizeMeasurementResult.Unavailable(destination.FailureReason);
        }

        if (!string.Equals(destination.ProviderName, _streamProviderName, StringComparison.Ordinal) ||
            destination.MeasurementState is not AzureQueueDestinationMeasurementState state)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                $"provider '{_streamProviderName}' destination is not an Azure Queue V2 adapter state");
        }

        try
        {
            var requestContext = state.RequestContext is null
                ? null
                : state.RequestContext.ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.Ordinal);
            var adapterText = state.Adapter.ToQueueMessage(
                state.StreamId,
                new[] { state.SerializedEvent },
                token: null,
                requestContext);
            var measured = Encoding.UTF8.GetByteCount(adapterText);
            var certifiedUpperBound = state.MessageEncoding == QueueMessageEncoding.Base64
                ? checked(4L * ((measured + 2L) / 3L))
                : measured;

            return ExecutorSizeMeasurementResult.CertifiedBound(
                measured,
                Math.Max(measured, certifiedUpperBound));
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException or OverflowException)
        {
            return ExecutorSizeMeasurementResult.Unavailable(
                $"Azure Queue V2 adapter measurement failed with {ex.GetType().Name}");
        }
    }

    internal static string ValidateProviderName(string streamProviderName) =>
        string.IsNullOrWhiteSpace(streamProviderName)
            ? throw new ArgumentException("The Orleans stream provider name must not be empty.", nameof(streamProviderName))
            : streamProviderName;
}

internal sealed record AzureQueueDestinationMeasurementState(
    IQueueDataAdapter<string, IBatchContainer> Adapter,
    StreamId StreamId,
    SerializableEvent SerializedEvent,
    IReadOnlyDictionary<string, object>? RequestContext,
    QueueMessageEncoding MessageEncoding);

internal sealed class OrleansAzureQueueDestinationMeasurementCapture : IOrleansDestinationMeasurementCapture
{
    private readonly IServiceProvider _services;
    private readonly string _streamProviderName;

    public OrleansAzureQueueDestinationMeasurementCapture(
        IServiceProvider services,
        string streamProviderName)
    {
        _services = services;
        _streamProviderName = OrleansAzureQueueStreamMessageSizeMeasurement.ValidateProviderName(streamProviderName);
    }

    public bool Matches(string providerName) =>
        string.Equals(providerName, _streamProviderName, StringComparison.Ordinal);

    public object? Capture(
        string providerName,
        string streamNamespace,
        Guid streamId,
        SerializableEvent serializedEvent,
        IReadOnlyDictionary<string, object>? requestContext,
        out string? failureReason)
    {
        failureReason = null;
        var adapter = _services.GetKeyedService<IQueueDataAdapter<string, IBatchContainer>>(providerName) ??
                      _services.GetService<IQueueDataAdapter<string, IBatchContainer>>();
        if (adapter is not AzureQueueDataAdapterV2)
        {
            failureReason = adapter is null
                ? $"named provider '{providerName}' has no keyed or unkeyed Azure Queue data adapter"
                : $"named provider '{providerName}' resolved {adapter.GetType().FullName}, not AzureQueueDataAdapterV2";
            return null;
        }

        var options = NamedOptionExtensions.GetOptionsByName<AzureQueueOptions>(_services, providerName);
        return new AzureQueueDestinationMeasurementState(
            adapter,
            StreamId.Create(streamNamespace, streamId),
            serializedEvent,
            requestContext,
            options.ClientOptions.MessageEncoding);
    }
}
