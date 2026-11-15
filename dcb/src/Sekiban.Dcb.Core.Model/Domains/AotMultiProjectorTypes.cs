using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     AOT-compatible implementation of ICoreMultiProjectorTypes.
///     Uses JsonTypeInfo for serialization instead of reflection.
/// </summary>
public sealed class AotMultiProjectorTypes : ICoreMultiProjectorTypes, IStreamingMultiProjectorTypes
{
    private static volatile bool _debugBypassProject;
    private readonly Dictionary<string, ProjectorRegistration> _projectors = new();
    private static readonly bool DebugBypassProject =
        Environment.GetEnvironmentVariable("SEKIBAN_WASM_DEBUG_BYPASS_MULTI_PROJECT") == "1";
    private static bool _debugBypassProjectSwitch;

    private record ProjectorRegistration(
        Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, IMultiProjectionPayload> Project,
        Func<IMultiProjectionPayload> GenerateInitial,
        string Version,
        Func<DcbDomainTypes, string, IMultiProjectionPayload, SerializationResult> Serialize,
        Func<Stream, DcbDomainTypes, string, IMultiProjectionPayload, SerializationSizeInfo> SerializeToStream,
        Func<DcbDomainTypes, string, byte[], IMultiProjectionPayload> Deserialize);

    /// <summary>
    ///     Register a multi-projector with AOT-compatible JsonTypeInfo.
    /// </summary>
    /// <typeparam name="TProjector">The projector type</typeparam>
    /// <param name="typeInfo">The JsonTypeInfo for serialization</param>
    public void RegisterProjector<TProjector>(JsonTypeInfo<TProjector> typeInfo)
        where TProjector : ICoreMultiProjector<TProjector>, new()
    {
        var name = TProjector.MultiProjectorName;

        _projectors[name] = new ProjectorRegistration(
            Project: (payload, ev, tags, domainTypes, safeWindowThreshold) =>
            {
                if (_debugBypassProject)
                {
                    return payload;
                }
                if (payload is TProjector typed)
                {
                    return TProjector.Project(typed, ev, tags, domainTypes, safeWindowThreshold);
                }
                throw new InvalidCastException($"Expected {typeof(TProjector).Name}");
            },
            GenerateInitial: () => TProjector.GenerateInitialPayload(),
            Version: TProjector.MultiProjectorVersion,
            Serialize: (domainTypes, safeWindowThreshold, payload) =>
            {
                var (compressed, originalSize) = GzipCompression.CompressJson((TProjector)payload, typeInfo);
                return new SerializationResult(compressed, originalSize, compressed.LongLength);
            },
            SerializeToStream: (destination, domainTypes, safeWindowThreshold, payload) =>
                MultiProjectorStreamSerializationHelper.WriteGzipJsonToStream(
                    destination,
                    (TProjector)payload,
                    typeInfo),
            Deserialize: (domainTypes, safeWindowThreshold, data) =>
            {
                // Legacy raw UTF-8 snapshots and gzip snapshots must remain equivalent to the stream helper, which
                // chooses gzip only from its two-byte marker.
                var jsonBytes = GzipCompression.DecompressIfGzip(data);
                return JsonSerializer.Deserialize(jsonBytes, typeInfo)!;
            });
        StreamingMultiProjectorTypesSupport.RegisterJsonTypeInfo(this, name, typeInfo);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public string GetProjectorVersion(string multiProjectorName) => GetRegistration(multiProjectorName).Version;

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllProjectorNames() => _projectors.Keys.ToList();

    /// <inheritdoc />
    public Func<IMultiProjectionPayload> GetInitialPayloadGenerator(string multiProjectorName) =>
        GetRegistration(multiProjectorName).GenerateInitial;

    /// <inheritdoc />
    public IMultiProjectionPayload GenerateInitialPayload(string multiProjectorName) =>
        GetRegistration(multiProjectorName).GenerateInitial();

    /// <inheritdoc />
    public SerializationResult Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload) =>
        GetRegistration(projectorName).Serialize(domainTypes, safeWindowThreshold, payload);

    /// <inheritdoc />
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

    /// <inheritdoc />
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
        var reg = GetRegistration(projectorName);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
        return reg.Deserialize(
            domainTypes,
            SortableUniqueId.MinValue.Value,
            GzipCompression.Compress(jsonBytes));
    }

    /// <summary>
    ///     Not supported in AOT mode.
    /// </summary>
    public Type GetProjectorType(string multiProjectorName) =>
        throw new NotSupportedException("GetProjectorType is not supported in AOT mode");

    /// <summary>
    ///     Not supported in AOT mode. Use Deserialize with DcbDomainTypes instead.
    /// </summary>
    public IMultiProjectionPayload Deserialize(byte[] data, string multiProjectorName, JsonSerializerOptions jsonOptions) =>
        throw new NotSupportedException("Use Deserialize with DcbDomainTypes");

    /// <summary>
    ///     Not supported in AOT mode. Use RegisterProjector with JsonTypeInfo instead.
    /// </summary>
    public bool RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        throw new NotSupportedException("Use RegisterProjector with JsonTypeInfo");

    private ProjectorRegistration GetRegistration(string projectorName) =>
        _projectors.TryGetValue(projectorName, out var reg)
            ? reg
            : throw new Exception($"Projector not found: {projectorName}");

    public static void SetDebugBypassProject(bool enabled) => _debugBypassProjectSwitch = enabled;

    public static bool GetDebugBypassProject() => _debugBypassProjectSwitch;
}
