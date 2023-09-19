using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
namespace Sekiban.Test.CosmosDb.Serializations;

public class InterfaceSerializationTest
{
    [Fact]
    public void DeserializationNotThrowsTest()
    {
        var test = JsonSerializer.Deserialize<TestRecord>("{\"Concept\":{\"$type\":\"Concept\",\"Value\":\"test\"}}");
        Assert.NotNull(test);
        Assert.Equal(new TestRecord(new Concept("test")), test);
    }
    [Fact]
    public void Deserialization2NotThrowsTest()
    {
        var test = JsonSerializer.Deserialize<TestRecord>("{\"Concept\":{\"$type\":\"Concept2\",\"Value2\":\"test2\"}}");
        Assert.NotNull(test);
        Assert.Equal(new TestRecord(new Concept2("test2")), test);
    }

    [Fact]
    public void SerializationSucceedsTest()
    {
        var test = new TestRecord(new Concept("test"));
        var json = JsonSerializer.Serialize(test);
        Assert.NotNull(json);
        Assert.Equal("{\"Concept\":{\"$type\":\"Concept\",\"Value\":\"test\"}}", json);
    }
    [Fact]
    public void SerializationSucceeds2Test()
    {
        var test = new TestRecord(new Concept2("test2"));
        var json = JsonSerializer.Serialize(test);
        Assert.NotNull(json);
        Assert.Equal("{\"Concept\":{\"$type\":\"Concept2\",\"Value2\":\"test2\"}}", json);
    }
    [JsonDerivedType(typeof(Concept), nameof(Concept))]
    [JsonDerivedType(typeof(Concept2), nameof(Concept2))]
    public interface IConcept;
    public record Concept(string Value) : IConcept;
    public record Concept2(string Value2) : IConcept;
    public record TestRecord(IConcept Concept);
}
