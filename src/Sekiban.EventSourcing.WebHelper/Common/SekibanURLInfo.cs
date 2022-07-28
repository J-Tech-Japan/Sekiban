namespace Sekiban.EventSourcing.WebHelper.Common;

public record SekibanURLInfo
{
    public string Url { get; init; }
    public string CommandType { get; init; }
}
