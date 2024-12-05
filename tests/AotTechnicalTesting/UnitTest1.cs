using AotTechnicalTesting.Definitions;
using System.Text.Json;
using Xunit.Abstractions;
namespace AotTechnicalTesting;

public class UnitTest1
{
    private readonly ITestOutputHelper _testOutputHelper;
    public UnitTest1(ITestOutputHelper testOutputHelper) { _testOutputHelper = testOutputHelper; }
    [Fact]
    public void Test1()
    {
        string jsonString;
        WeatherForecast weatherForecast = new()
            { Date = DateTime.Parse("2019-08-01"), TemperatureCelsius = 25, Summary = "Hot" };

        // Serialize using TypeInfo<TValue> provided by the context
        // and options specified by [JsonSourceGenerationOptions].
        jsonString = JsonSerializer.Serialize(
            weatherForecast, SerializationModeOptionsContext.Default.WeatherForecast);
        _testOutputHelper.WriteLine(jsonString);

        // In AOT environment below code will not work
        // var jsonString2 = JsonSerializer.Serialize(weatherForecast,typeof(WeatherForecast));
        // _testOutputHelper.WriteLine(jsonString2);
    }
}
