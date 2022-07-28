namespace Sekiban.EventSourcing.WebHelper.Common;

public record SekibanURLInfo
{
    public string Method { get; set; }
    public string Url { get; init; }
    public string JsonBodyType { get; init; }
}
