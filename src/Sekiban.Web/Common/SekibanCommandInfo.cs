namespace Sekiban.Web.Common;

public record SekibanCommandInfo
{
    public string Method { get; set; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string JsonBodyType { get; init; } = string.Empty;
    public dynamic SampleBodyObject { get; init; } = string.Empty;
    public dynamic SampleResponseObject { get; init; } = string.Empty;
    public bool IsCreateEvent { get; init; } = false;
}
