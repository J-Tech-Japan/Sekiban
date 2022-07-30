namespace Sekiban.EventSourcing.WebHelper.Common;

public record SekibanCommandInfo
{
    public string Method { get; set; }
    public string Url { get; init; }
    public string JsonBodyType { get; init; }
    public dynamic SampleBodyObject { get; set; }
    public dynamic SampleResponseObject { get; set; }
    public bool IsCreateEvent { get; set; }
}
