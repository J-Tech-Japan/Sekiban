using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.StreamingRestore.Legacy1018Fixture;

/// <summary>
///     A real separately compiled v10.18 external registry. It knows only the published
///     <see cref="ICoreMultiProjectorTypes" /> contract and intentionally contains no reference to the newer optional
///     streaming capability.
/// </summary>
public sealed class Legacy1018ExternalRegistry : ICoreMultiProjectorTypes
{
    public const string ProjectorName = "legacy-1018-external-registry";
    public const string ProjectorVersion = "10.18";

    public int DeserializeCalls { get; private set; }

    public ResultBox<IMultiProjectionPayload> Project(
        string multiProjectorName,
        IMultiProjectionPayload payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold) => ResultBox.FromValue(payload);

    public ResultBox<string> GetProjectorVersion(string multiProjectorName) =>
        multiProjectorName == ProjectorName
            ? ResultBox.FromValue(ProjectorVersion)
            : ResultBox.Error<string>(new InvalidOperationException($"Unknown projector '{multiProjectorName}'."));

    public IReadOnlyList<string> GetAllProjectorNames() => [ProjectorName];

    public ResultBox<Func<IMultiProjectionPayload>> GetInitialPayloadGenerator(string multiProjectorName) =>
        multiProjectorName == ProjectorName
            ? ResultBox.FromValue<Func<IMultiProjectionPayload>>(() => new Legacy1018Payload(string.Empty))
            : ResultBox.Error<Func<IMultiProjectionPayload>>(new InvalidOperationException("Unknown projector."));

    public ResultBox<Type> GetProjectorType(string multiProjectorName) =>
        multiProjectorName == ProjectorName
            ? ResultBox.FromValue(typeof(Legacy1018Payload))
            : ResultBox.Error<Type>(new InvalidOperationException("Unknown projector."));

    public ResultBox<IMultiProjectionPayload> GenerateInitialPayload(string multiProjectorName) =>
        multiProjectorName == ProjectorName
            ? ResultBox.FromValue<IMultiProjectionPayload>(new Legacy1018Payload(string.Empty))
            : ResultBox.Error<IMultiProjectionPayload>(new InvalidOperationException("Unknown projector."));

    public ResultBox<IMultiProjectionPayload> Deserialize(
        byte[] data,
        string multiProjectorName,
        JsonSerializerOptions jsonOptions) => DeserializeCore(multiProjectorName, data);

    public ResultBox<IMultiProjectionPayload> Deserialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        byte[] data) => DeserializeCore(projectorName, data);

    public ResultBox<IMultiProjectionPayload> DeserializeJson(
        string projectorName,
        string json,
        DcbDomainTypes domainTypes) => DeserializeCore(projectorName, Encoding.UTF8.GetBytes(json));

    public ResultBox<SerializationResult> Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload)
    {
        if (projectorName != ProjectorName || payload is not Legacy1018Payload typed)
        {
            return ResultBox.Error<SerializationResult>(new InvalidOperationException("Unexpected legacy payload."));
        }

        var bytes = Encoding.UTF8.GetBytes(typed.Value);
        return ResultBox.FromValue(new SerializationResult(bytes, bytes.Length, bytes.Length));
    }

    public ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        ResultBox.Error<bool>(new NotSupportedException("The frozen 10.18 fixture has no custom registrations."));

    private ResultBox<IMultiProjectionPayload> DeserializeCore(string projectorName, byte[] data)
    {
        if (projectorName != ProjectorName)
        {
            return ResultBox.Error<IMultiProjectionPayload>(new InvalidOperationException("Unknown projector."));
        }

        DeserializeCalls++;
        return ResultBox.FromValue<IMultiProjectionPayload>(new Legacy1018Payload(Encoding.UTF8.GetString(data)));
    }
}

public sealed record Legacy1018Payload(string Value) : IMultiProjectionPayload;
