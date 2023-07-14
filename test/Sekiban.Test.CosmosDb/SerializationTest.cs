using System.Text.Json;
using Xunit;
namespace Sekiban.Test.CosmosDb;

public class SerializationTest
{


    [Fact]
    public void SerializationNotThrowsTest()
    {
        var test = JsonSerializer.Deserialize<TestRecord>("{\"Name\":\"test\",\"Address\":{\"Value\":\"test\"}}");
        Assert.NotNull(test);

        Assert.Throws<JsonException>(
            () =>
            {
                JsonSerializer.Deserialize<TestRecord>("{\"Name\":\"test\",\"Address\":\"test\"}");
            });
    }
    public record ValueObject(string Value);
    public record TestRecord(string Name, ValueObject Address);
}
