using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     AOT-compatible implementation of ICoreMultiProjectorTypes for exception-based projectors.
/// </summary>
public sealed class AotWithoutResultMultiProjectorTypes : ICoreMultiProjectorTypes, IStreamingMultiProjectorTypes
{
    private static readonly bool DebugBypassProject =
        Environment.GetEnvironmentVariable("SEKIBAN_WASM_DEBUG_BYPASS_MULTI_PROJECT") == "1";
    private static bool _debugBypassProjectSwitch;
    private readonly Dictionary<string, ProjectorRegistration> _projectors = new();

    private sealed record ProjectorRegistration(
        Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, IMultiProjectionPayload> Project,
        Func<IMultiProjectionPayload> GenerateInitial,
        string Version,
        Func<DcbDomainTypes, string, IMultiProjectionPayload, SerializationResult> Serialize,
        Func<Stream, DcbDomainTypes, string, IMultiProjectionPayload, SerializationSizeInfo> SerializeToStream,
        Func<DcbDomainTypes, string, byte[], IMultiProjectionPayload> Deserialize);

    public void RegisterProjector<TProjector>(JsonTypeInfo<TProjector> typeInfo)
        where TProjector : IMultiProjector<TProjector>, new()
    {
        string name = TProjector.MultiProjectorName;

        _projectors[name] = new ProjectorRegistration(
            Project: (payload, ev, tags, domainTypes, safeWindowThreshold) =>
            {
                if (payload is not TProjector typed)
                {
                    throw new InvalidCastException($"Expected {typeof(TProjector).Name}");
                }

                return TProjector.Project(typed, ev, tags, domainTypes, safeWindowThreshold);
            },
            GenerateInitial: () => TProjector.GenerateInitialPayload(),
            Version: TProjector.MultiProjectorVersion,
            Serialize: (_, _, payload) =>
            {
                var typed = (TProjector)payload;
                var (compressed, uncompressedLength) = GzipCompression.CompressJson(typed, typeInfo);
                return new SerializationResult(compressed, uncompressedLength, compressed.LongLength);
            },
            SerializeToStream: (destination, _, _, payload) =>
                MultiProjectorStreamSerializationHelper.WriteGzipJsonToStream(
                    destination,
                    (TProjector)payload,
                    typeInfo),
            Deserialize: (_, _, data) =>
            {
                byte[] jsonBytes = GzipCompression.DecompressIfGzip(data);
                return JsonSerializer.Deserialize(jsonBytes, typeInfo)
                    ?? throw new InvalidOperationException($"Failed to deserialize {typeof(TProjector).Name}");
            });
        StreamingMultiProjectorTypesSupport.RegisterJsonTypeInfo(this, name, typeInfo);
    }

    public IMultiProjectionPayload Project(
        string multiProjectorName,
        IMultiProjectionPayload payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        if (DebugBypassProject || _debugBypassProjectSwitch)
        {
            return payload;
        }

        return GetRegistration(multiProjectorName).Project(payload, ev, tags, domainTypes, safeWindowThreshold);
    }

    public string GetProjectorVersion(string multiProjectorName) => GetRegistration(multiProjectorName).Version;

    public IReadOnlyList<string> GetAllProjectorNames() => _projectors.Keys.ToList();

    public Func<IMultiProjectionPayload> GetInitialPayloadGenerator(string multiProjectorName) =>
        GetRegistration(multiProjectorName).GenerateInitial;

    public Type GetProjectorType(string multiProjectorName) =>
        throw new NotSupportedException("GetProjectorType is not supported in AOT mode");

    public IMultiProjectionPayload GenerateInitialPayload(string multiProjectorName) =>
        GetRegistration(multiProjectorName).GenerateInitial();

    public IMultiProjectionPayload Deserialize(
        byte[] data,
        string multiProjectorName,
        JsonSerializerOptions jsonOptions) =>
        throw new NotSupportedException("Use Deserialize with DcbDomainTypes");

    public IMultiProjectionPayload Deserialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        byte[] data) =>
        GetRegistration(projectorName).Deserialize(domainTypes, safeWindowThreshold, data);

    public bool SupportsStreamDeserialization(string projectorName) => StreamingMultiProjectorTypesSupport.Supports(this, projectorName);

    public Task<IMultiProjectionPayload> DeserializeFromStreamAsync(string projectorName, DcbDomainTypes domainTypes, string safeWindowThreshold, Stream source, CancellationToken cancellationToken = default) => StreamingMultiProjectorTypesSupport.DeserializeWithProjectorNotFoundAsync(this, projectorName, domainTypes, safeWindowThreshold, source, cancellationToken);

    public IMultiProjectionPayload DeserializeJson(
        string projectorName,
        string json,
        DcbDomainTypes domainTypes)
    {
        ProjectorRegistration reg = GetRegistration(projectorName);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        return reg.Deserialize(
            domainTypes,
            SortableUniqueId.MinValue.Value,
            GzipCompression.Compress(jsonBytes));
    }

    public SerializationResult Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload) =>
        GetRegistration(projectorName).Serialize(domainTypes, safeWindowThreshold, payload);

    public SerializationSizeInfo SerializeToStream(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload,
        Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        return GetRegistration(projectorName).SerializeToStream(destination, domainTypes, safeWindowThreshold, payload);
    }

    public bool RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        throw new NotSupportedException("Use RegisterProjector with JsonTypeInfo");

    private ProjectorRegistration GetRegistration(string projectorName) =>
        _projectors.TryGetValue(projectorName, out ProjectorRegistration? reg)
            ? reg
            : throw new Exception($"Projector not found: {projectorName}");

    public static void SetDebugBypassProject(bool enabled) => _debugBypassProjectSwitch = enabled;

    public static bool GetDebugBypassProject() => _debugBypassProjectSwitch;
}
