using Sekiban.Dcb.Tags;

namespace Dcb.Domain.Weather;

public record WeatherForecastTag(Guid ForecastId) : ITagGroup<WeatherForecastTag>
{
    public static string TagGroupName => "WeatherForecast";
    
    public bool IsConsistencyTag() => true;
    public string GetTagContent() => ForecastId.ToString();
    
    public static WeatherForecastTag FromContent(string content)
    {
        if (Guid.TryParse(content, out var forecastId))
        {
            return new WeatherForecastTag(forecastId);
        }
        throw new ArgumentException($"Invalid forecast ID format: {content}");
    }
}