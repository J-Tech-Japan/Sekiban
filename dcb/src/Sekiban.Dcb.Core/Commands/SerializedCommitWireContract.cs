using System.Text.Encodings.Web;
using System.Text.Json;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     SEK-G17 canonical serializer for the serialized-commit wire contract. It exposes the NORMATIVE
///     <see cref="JsonSerializerOptions" /> that fully pins the production wire bytes:
///     <list type="bullet">
///         <item>property naming policy: camelCase (from <see cref="SerializedCommitWireJsonContext" />);</item>
///         <item>property order: declaration / constructor-parameter order (source-gen metadata order);</item>
///         <item>indentation: none; UTF-8, no BOM, no insignificant whitespace;</item>
///         <item>encoder: <see cref="JavaScriptEncoder.Default" /> — non-ASCII and HTML-sensitive characters are escaped
///         (byte-for-byte identical to the ASP.NET <c>JsonSerializerDefaults.Web</c> write path);</item>
///         <item>null/default handling: values are always written (never ignored);</item>
///         <item><c>byte[]</c> payloads: base64 strings.</item>
///     </list>
///     Endpoints that claim to speak this contract must serialize/deserialize through <see cref="Options" /> (or reproduce
///     these exact settings); the frozen golden vectors verify the byte shape and fail CI on any drift.
/// </summary>
public static class SerializedCommitWireContract
{
    /// <summary>The normative, read-only wire options. Contract-owned; requires no attributes on the DTOs.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Serializes a wire DTO to its canonical UTF-8 bytes (no BOM).</summary>
    public static byte[] SerializeToUtf8Bytes<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        // Start from the source-gen context's options (camelCase / order / non-indented / never-ignore already baked in),
        // then pin the encoder explicitly so the normative behavior is self-evident rather than relying on the ambient
        // default. The result is made read-only so the pinned contract cannot be mutated by a consumer.
        var options = new JsonSerializerOptions(SerializedCommitWireJsonContext.Default.Options)
        {
            Encoder = JavaScriptEncoder.Default
        };
        options.MakeReadOnly();
        return options;
    }
}
