namespace Sekiban.Addon.Web.Common;

public record SekibanAggregateInfo(string AggregateName, SekibanQueryInfo QueryInfo, List<SekibanCommandInfo> commands);
