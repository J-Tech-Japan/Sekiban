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
///     AOT-compatible implementation of ICoreMultiProjectorTypes for exception-based projectors.
/// </summary>
public sealed class AotWithoutResultMultiProjectorTypes : ICoreMultiProjectorTypes
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
                string json = JsonSerializer.Serialize((TProjector)payload, typeInfo);
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                byte[] compressed = GzipCompression.Compress(bytes);
                return new SerializationResult(compressed, bytes.LongLength, compressed.LongLength);
            },
            Deserialize: (_, _, data) =>
            {
                byte[] jsonBytes = GzipCompression.Decompress(data);
                return JsonSerializer.Deserialize(jsonBytes, typeInfo)
                    ?? throw new InvalidOperationException($"Failed to deserialize {typeof(TProjector).Name}");
            });
    }

    public ResultBox<IMultiProjectionPayload> Project(
        string multiProjectorName,
        IMultiProjectionPayload payload,
        Event ev,
        List<ITag> tags,
        DcbDomainTypes domainTypes,
        SortableUniqueId safeWindowThreshold)
    {
        try
        {
            if (DebugBypassProject || _debugBypassProjectSwitch)
            {
                return ResultBox.FromValue(payload);
            }

            if (_projectors.TryGetValue(multiProjectorName, out ProjectorRegistration? reg))
            {
                return ResultBox.FromValue(reg.Project(payload, ev, tags, domainTypes, safeWindowThreshold));
            }

            return ResultBox.Error<IMultiProjectionPayload>(
                new Exception($"Projector not found: {multiProjectorName}"));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectionPayload>(ex);
        }
    }

    public ResultBox<string> GetProjectorVersion(string multiProjectorName)
    {
        if (_projectors.TryGetValue(multiProjectorName, out ProjectorRegistration? reg))
        {
            return ResultBox.FromValue(reg.Version);
        }

        return ResultBox.Error<string>(new Exception($"Projector not found: {multiProjectorName}"));
    }

    public IReadOnlyList<string> GetAllProjectorNames() => _projectors.Keys.ToList();

    public ResultBox<Func<IMultiProjectionPayload>> GetInitialPayloadGenerator(string multiProjectorName)
    {
        if (_projectors.TryGetValue(multiProjectorName, out ProjectorRegistration? reg))
        {
            return ResultBox.FromValue(reg.GenerateInitial);
        }

        return ResultBox.Error<Func<IMultiProjectionPayload>>(
            new Exception($"Projector not found: {multiProjectorName}"));
    }

    public ResultBox<Type> GetProjectorType(string multiProjectorName) =>
        ResultBox.Error<Type>(new NotSupportedException("GetProjectorType is not supported in AOT mode"));

    public ResultBox<IMultiProjectionPayload> GenerateInitialPayload(string multiProjectorName)
    {
        if (_projectors.TryGetValue(multiProjectorName, out ProjectorRegistration? reg))
        {
            return ResultBox.FromValue(reg.GenerateInitial());
        }

        return ResultBox.Error<IMultiProjectionPayload>(
            new Exception($"Projector not found: {multiProjectorName}"));
    }

    public ResultBox<IMultiProjectionPayload> Deserialize(
        byte[] data,
        string multiProjectorName,
        JsonSerializerOptions jsonOptions) =>
        ResultBox.Error<IMultiProjectionPayload>(new NotSupportedException("Use Deserialize with DcbDomainTypes"));

    public ResultBox<IMultiProjectionPayload> Deserialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        byte[] data)
    {
        if (_projectors.TryGetValue(projectorName, out ProjectorRegistration? reg))
        {
            return ResultBox.FromValue(reg.Deserialize(domainTypes, safeWindowThreshold, data));
        }

        return ResultBox.Error<IMultiProjectionPayload>(new Exception($"Projector not found: {projectorName}"));
    }

    public ResultBox<IMultiProjectionPayload> DeserializeJson(
        string projectorName,
        string json,
        DcbDomainTypes domainTypes)
    {
        if (!_projectors.TryGetValue(projectorName, out ProjectorRegistration? reg))
        {
            return ResultBox.Error<IMultiProjectionPayload>(new Exception($"Projector not found: {projectorName}"));
        }

        try
        {
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            return ResultBox.FromValue(reg.Deserialize(
                domainTypes,
                SortableUniqueId.MinValue.Value,
                GzipCompression.Compress(jsonBytes)));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<IMultiProjectionPayload>(ex);
        }
    }
    public ResultBox<SerializationResult> Serialize(
        string projectorName,
        DcbDomainTypes domainTypes,
        string safeWindowThreshold,
        IMultiProjectionPayload payload)
    {
        if (_projectors.TryGetValue(projectorName, out ProjectorRegistration? reg))
        {
            return ResultBox.FromValue(reg.Serialize(domainTypes, safeWindowThreshold, payload));
        }

        return ResultBox.Error<SerializationResult>(new Exception($"Projector not found: {projectorName}"));
    }

    public ResultBox<bool> RegisterProjectorWithCustomSerialization<T>()
        where T : ICoreMultiProjectorWithCustomSerialization<T>, new() =>
        ResultBox.Error<bool>(new NotSupportedException("Use RegisterProjector with JsonTypeInfo"));

    public static void SetDebugBypassProject(bool enabled) => _debugBypassProjectSwitch = enabled;

    public static bool GetDebugBypassProject() => _debugBypassProjectSwitch;
}
