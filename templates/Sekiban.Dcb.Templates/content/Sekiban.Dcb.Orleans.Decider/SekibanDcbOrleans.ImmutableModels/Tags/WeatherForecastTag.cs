using Sekiban.Dcb.Tags;
namespace Dcb.ImmutableModels.Tags;

public record WeatherForecastTag(Guid ForecastId) : IGuidTagGroup<WeatherForecastTag>
{
    public static string TagGroupName => "WeatherForecast";

    public bool IsConsistencyTag() => true;
    public static WeatherForecastTag FromContent(string content)
    {
        if (Guid.TryParse(content, out var forecastId))
        {
            return new WeatherForecastTag(forecastId);
        }
        throw new ArgumentException($"Invalid forecast ID format: {content}");
    }
    public Guid GetId() => ForecastId;
}
