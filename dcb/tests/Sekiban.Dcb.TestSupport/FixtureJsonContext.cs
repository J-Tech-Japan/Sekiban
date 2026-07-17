using System.Text.Json.Serialization;
namespace Sekiban.Dcb.TestSupport;

/// <summary>
///     Source-generated <c>JsonTypeInfo</c> for the fixture payloads, so the AOT pipeline can be tested with real
///     source-gen metadata rather than a reflection stand-in. camelCase to match the canonical write path — the same
///     naming the reflection side uses, so the two pipelines are compared on equal terms.
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FixtureStudentCreated))]
[JsonSerializable(typeof(FixtureStudentWithRequired))]
[JsonSerializable(typeof(FixtureAmbiguousCasing))]
public partial class FixtureJsonContext : JsonSerializerContext;
