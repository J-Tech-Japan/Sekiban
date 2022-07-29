namespace Sekiban.EventSourcing.WebHelper.Common;

public record SekibanQueryInfo
{
    public string Method { get; set; }
    public string Url { get; init; }
    public dynamic SampleResponseObject { get; set; }
    public string AggregateType { get; set; }
}
