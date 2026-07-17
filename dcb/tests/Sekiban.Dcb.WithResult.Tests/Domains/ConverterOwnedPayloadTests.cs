using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.TestSupport;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
namespace Sekiban.Dcb.WithResult.Tests.Domains;

/// <summary>
///     When a custom converter owns a payload's binding, its <c>JsonTypeInfo</c> is <c>Kind=None</c> with no declared
///     members. That is not a name contract the preflight can validate — the converter can read whatever top-level
///     keys it wants — so a policy that promises name validation must fail deterministically rather than wave the
///     payload through while claiming to have checked it. The one policy that promises nothing, CompatibleCaseSensitive,
///     must still let the converter run.
///     Covered for both pipelines: a Simple caller converter, and a converter-owned JsonTypeInfo registered into the
///     AOT pipeline.
/// </summary>
public class ConverterOwnedPayloadTests
{
    /// <summary>The marker the converter stamps, so a test can prove the converter actually ran.</summary>
    private const string ConverterMarker = "bound-by-converter";

    private static readonly Guid Id = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly string AnyPayload =
        $$"""{"studentId":"{{Id}}","name":"Alice","maxClassCount":5}""";

    private static JsonSerializerOptions ConverterOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };
        options.Converters.Add(new MarkerConverter());
        return options;
    }

    // --- Simple pipeline ---

    [Theory]
    [InlineData(EventPayloadDeserializationPolicy.FailOnCaseMismatch)]
    [InlineData(EventPayloadDeserializationPolicy.StrictUnmapped)]
    [InlineData(EventPayloadDeserializationPolicy.CaseInsensitiveLegacy)]
    public void Simple_ConverterOwnedPayload_FailsDeterministically_UnderMetadataRequiringPolicies(
        EventPayloadDeserializationPolicy policy)
    {
        var types = new SimpleEventTypes(ConverterOptions(), policy);
        types.RegisterEventType<FixtureStudentCreated>(MalformedEventPayloadFixture.EventTypeName);

        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => types.DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, AnyPayload));

        Assert.Contains("custom converter", ex.Message);
        Assert.Contains("JsonTypeInfoKind.None", ex.Message);
    }

    [Fact]
    public void Simple_ConverterOwnedPayload_Binds_UnderCompatibleCaseSensitive_AndTheConverterRuns()
    {
        var types = new SimpleEventTypes(ConverterOptions(), EventPayloadDeserializationPolicy.CompatibleCaseSensitive);
        types.RegisterEventType<FixtureStudentCreated>(MalformedEventPayloadFixture.EventTypeName);

        var payload = types.DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, AnyPayload);

        // The converter ran — the marker proves the escape hatch actually bound through it rather than being skipped.
        var student = Assert.IsType<FixtureStudentCreated>(payload);
        Assert.Equal(ConverterMarker, student.Name);
    }

    // --- AOT pipeline: a converter-owned JsonTypeInfo registered directly ---

    [Theory]
    [InlineData(EventPayloadDeserializationPolicy.FailOnCaseMismatch)]
    [InlineData(EventPayloadDeserializationPolicy.StrictUnmapped)]
    [InlineData(EventPayloadDeserializationPolicy.CaseInsensitiveLegacy)]
    public void Aot_ConverterOwnedTypeInfo_FailsDeterministically_UnderMetadataRequiringPolicies(
        EventPayloadDeserializationPolicy policy)
    {
        var options = ConverterOptions();
        var aot = new AotEventTypes(options, policy);
        aot.Register(
            MalformedEventPayloadFixture.EventTypeName,
            typeof(FixtureStudentCreated),
            options.GetTypeInfo(typeof(FixtureStudentCreated)));

        var ex = Assert.Throws<SekibanEventPayloadBindingException>(
            () => aot.DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, AnyPayload));

        Assert.Contains("custom converter", ex.Message);
    }

    [Fact]
    public void Aot_ConverterOwnedTypeInfo_Binds_UnderCompatibleCaseSensitive_AndTheConverterRuns()
    {
        var options = ConverterOptions();
        var aot = new AotEventTypes(options, EventPayloadDeserializationPolicy.CompatibleCaseSensitive);
        aot.Register(
            MalformedEventPayloadFixture.EventTypeName,
            typeof(FixtureStudentCreated),
            options.GetTypeInfo(typeof(FixtureStudentCreated)));

        var payload = aot.DeserializeEventPayload(MalformedEventPayloadFixture.EventTypeName, AnyPayload);

        var student = Assert.IsType<FixtureStudentCreated>(payload);
        Assert.Equal(ConverterMarker, student.Name);
    }

    /// <summary>
    ///     A converter that owns <see cref="FixtureStudentCreated" />'s binding. It reads the object (a known
    ///     top-level shape) but stamps a marker name, so a test can tell whether it ran. Its presence makes the type's
    ///     JsonTypeInfo Kind=None with no declared properties.
    /// </summary>
    private sealed class MarkerConverter : JsonConverter<FixtureStudentCreated>
    {
        public override FixtureStudentCreated Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            using var document = JsonDocument.ParseValue(ref reader);
            var studentId = document.RootElement.TryGetProperty("studentId", out var idElement)
                ? idElement.GetGuid()
                : Guid.Empty;
            return new FixtureStudentCreated(studentId, ConverterMarker, 0);
        }

        public override void Write(
            Utf8JsonWriter writer,
            FixtureStudentCreated value,
            JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("studentId", value.StudentId);
            writer.WriteString("name", value.Name);
            writer.WriteNumber("maxClassCount", value.MaxClassCount);
            writer.WriteEndObject();
        }
    }
}
