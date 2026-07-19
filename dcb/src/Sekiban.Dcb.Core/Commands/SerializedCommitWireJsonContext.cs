using System.Text.Json.Serialization;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Commands;

/// <summary>
///     SEK-G17 contract-OWNED source-generated <c>JsonTypeInfo</c> metadata for the serialized-commit wire contract. This is
///     the additive pinning surface: the camelCase naming, property (declaration) order, non-indented layout and
///     never-ignore semantics are declared HERE, on the contract, rather than as attributes on the existing positional
///     DTOs. Because nothing is stamped on the DTOs, legacy ambient serialization through an arbitrary
///     <see cref="System.Text.Json.JsonSerializerOptions" /> (including a fresh, PascalCase one) is left byte-for-byte
///     unchanged; only code that opts into <see cref="SerializedCommitWireContract.Options" /> gets the pinned wire shape.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(SerializedCommitRequest))]
[JsonSerializable(typeof(VersionedSerializedCommitRequest))]
[JsonSerializable(typeof(SerializableEventCandidate))]
[JsonSerializable(typeof(ConsistencyTagEntry))]
public partial class SerializedCommitWireJsonContext : JsonSerializerContext;
