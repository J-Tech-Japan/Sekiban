namespace Sekiban.EventSourcing.WebHelper.Common;

public record SekibanAggregateInfo(string AggregateName, SekibanQueryInfo QueryInfo, List<SekibanCommandInfo> commands);
