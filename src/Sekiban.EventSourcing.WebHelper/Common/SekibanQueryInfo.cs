namespace Sekiban.EventSourcing.WebHelper.Common
{
    public record SekibanQueryInfo
    {
        public string Method { get; set; }
        public string GetUrl { get; init; }
        public string ListUrl { get; init; }
        public string GetEventsUrl { get; init; }
        public string GetCommandsUrl { get; init; }
        public dynamic SampleResponseObject { get; set; }
    }
}
