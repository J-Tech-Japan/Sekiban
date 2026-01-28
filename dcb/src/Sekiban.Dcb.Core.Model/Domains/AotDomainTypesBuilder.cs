using System.Text.Json;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Builder for creating DcbDomainTypes with AOT-compatible implementations.
///     Use this builder in WASM/NativeAOT environments where reflection is not available.
/// </summary>
public class AotDomainTypesBuilder
{
    /// <summary>
    ///     Gets the AOT-compatible event types registry.
    /// </summary>
    public AotEventTypes EventTypes { get; } = new();

    /// <summary>
    ///     Gets the AOT-compatible tag types registry.
    /// </summary>
    public AotTagTypes TagTypes { get; } = new();

    /// <summary>
    ///     Gets the AOT-compatible tag projector types registry.
    /// </summary>
    public AotTagProjectorTypes TagProjectorTypes { get; } = new();

    /// <summary>
    ///     Gets the AOT-compatible tag state payload types registry.
    /// </summary>
    public AotTagStatePayloadTypes TagStatePayloadTypes { get; } = new();

    /// <summary>
    ///     Gets the AOT-compatible multi-projector types registry.
    /// </summary>
    public AotMultiProjectorTypes MultiProjectorTypes { get; } = new();

    /// <summary>
    ///     Gets the AOT-compatible query types registry.
    /// </summary>
    public AotQueryTypes QueryTypes { get; } = new();

    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    ///     Initializes a new instance of AotDomainTypesBuilder.
    /// </summary>
    /// <param name="options">Optional JSON serializer options. If not provided, defaults are used.</param>
    public AotDomainTypesBuilder(JsonSerializerOptions? options = null)
    {
        _jsonOptions = options ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    ///     Builds the DcbDomainTypes with all registered types.
    /// </summary>
    /// <returns>A configured DcbDomainTypes instance</returns>
    public DcbDomainTypes Build() => new(
        EventTypes,
        TagTypes,
        TagProjectorTypes,
        TagStatePayloadTypes,
        MultiProjectorTypes,
        QueryTypes,
        _jsonOptions);
}
