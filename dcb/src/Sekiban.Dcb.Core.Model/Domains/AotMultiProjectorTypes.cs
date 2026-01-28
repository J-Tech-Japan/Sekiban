using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     AOT-compatible implementation of ICoreMultiProjectorTypes.
///     Uses JsonTypeInfo for serialization instead of reflection.
/// </summary>
public sealed class AotMultiProjectorTypes : ICoreMultiProjectorTypes
{
    private readonly Dictionary<string, ProjectorRegistration> _projectors = new();

    private record ProjectorRegistration(
        Func<IMultiProjectionPayload, Event, List<ITag>, DcbDomainTypes, SortableUniqueId, ResultBox<IMultiProjectionPayload>> Project,
        Func<IMultiProjectionPayload> GenerateInitial,
        string Version,
        Func<DcbDomainTypes, string, IMultiProjectionPayload, SerializationResult> Serialize,
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
                if (payload is TProjector typed)
                {
                    return TProjector.Project(typed, ev, tags, domainTypes, safeWindowThreshold)
                        .Remap(p => (IMultiProjectionPayload)p);
                }
                return ResultBox.Error<IMultiProjectionPayload>(
                    new InvalidCastException($"Expected {typeof(TProjector).Name}"));
            },
            GenerateInitial: () => TProjector.GenerateInitialPayload(),
            Version: TProjector.MultiProjectorVersion,
            Serialize: (domainTypes, safeWindowThreshold, payload) =>
            {
                var json = JsonSerializer.Serialize((TProjector)payload, typeInfo);
                var bytes = Encoding.UTF8.GetBytes(json);
                var compressed = GzipCompression.Compress(bytes);
                return new SerializationResult(compressed, bytes.LongLength, compressed.LongLength);
            },
            Deserialize: (domainTypes, safeWindowThreshold, data) =>
            {
                var jsonBytes = GzipCompression.Decompress(data);
                return JsonSerializer.Deserialize(jsonBytes, typeInfo)!;
            }
        );
    }

    /// <inheritdoc />
    public ResultBox<IMultiProjectionPayload> Project(
        string multiProjectorName,
        IMultiProjectionPayload payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        if (_projectors.TryGetValue(multiProjectorName, out var reg))
            return reg.Project(payload, ev, tags, domainTypes, safeWindowThreshold);
        return ResultBox.Error<IMultiProjectionPayload>(new Exception($"Projector not found: {multiProjectorName}"));
    }

    /// <inheritdoc />
    public ResultBox<string> GetProjectorVersion(string multiProjectorName)
    {
        if (_projectors.TryGetValue(multiProjectorName, out var reg))
            return ResultBox.FromValue(reg.Version);
        return ResultBox.Error<string>(new Exception($"Projector not found: {multiProjectorName}"));
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetAllProjectorNames() => _projectors.Keys.ToList();

    /// <inheritdoc />
    public ResultBox<Func<IMultiProjectionPayload>> GetInitialPayloadGenerator(string multiProjectorName)
    {
        if (_projectors.TryGetValue(multiProjectorName, out var reg))
            return ResultBox.FromValue(reg.GenerateInitial);
        return ResultBox.Error<Func<IMultiProjectionPayload>>(new Exception($"Projector not found: {multiProjectorName}"));
    }

    /// <inheritdoc />
    public ResultBox<IMultiProjectionPayload> GenerateInitialPayload(string multiProjectorName)
    {
        if (_projectors.TryGetValue(multiProjectorName, out var reg))
            return ResultBox.FromValue(reg.GenerateInitial());
        return ResultBox.Error<IMultiProjectionPayload>(new Exception($"Projector not found: {multiProjectorName}"));
    }

    /// <inheritdoc />
    public ResultBox<SerializationResult> Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload)
    {
        if (_projectors.TryGetValue(projectorName, out var reg))
            return ResultBox.FromValue(reg.Serialize(domainTypes, safeWindowThreshold, payload));
        return ResultBox.Error<SerializationResult>(new Exception($"Projector not found: {projectorName}"));
    }

    /// <inheritdoc />
    public ResultBox<IMultiProjectionPayload> Deserialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        byte[] data)
    {
        if (_projectors.TryGetValue(projectorName, out var reg))
            return ResultBox.FromValue(reg.Deserialize(domainTypes, safeWindowThreshold, data));
        return ResultBox.Error<IMultiProjectionPayload>(new Exception($"Projector not found: {projectorName}"));
    }

    /// <summary>
    ///     Not supported in AOT mode.
    /// </summary>
    public ResultBox<Type> GetProjectorType(string multiProjectorName) =>
        ResultBox.Error<Type>(new NotSupportedException("GetProjectorType is not supported in AOT mode"));

    /// <summary>
    ///     Not supported in AOT mode. Use Deserialize with DcbDomainTypes instead.
    /// </summary>
    public ResultBox<IMultiProjectionPayload> Deserialize(byte[] data, string multiProjectorName, JsonSerializerOptions jsonOptions) =>
        ResultBox.Error<IMultiProjectionPayload>(new NotSupportedException("Use Deserialize with DcbDomainTypes"));

    /// <summary>
    ///     Not supported in AOT mode. Use RegisterProjector with JsonTypeInfo instead.
    /// </summary>
    public ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        ResultBox.Error<bool>(new NotSupportedException("Use RegisterProjector with JsonTypeInfo"));
}
