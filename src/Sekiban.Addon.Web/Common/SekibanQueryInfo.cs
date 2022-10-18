namespace Sekiban.Addon.Web.Common;

public record SekibanQueryInfo
{
    public string Method { get; init; } = string.Empty;
    public string GetUrl { get; init; } = string.Empty;
    public string ListUrl { get; init; } = string.Empty;
    public string GetEventsUrl { get; init; } = string.Empty;
    public string GetCommandsUrl { get; init; } = string.Empty;
    public dynamic SampleResponseObject { get; init; } = string.Empty;
}
