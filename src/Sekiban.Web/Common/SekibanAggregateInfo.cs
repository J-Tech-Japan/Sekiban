namespace Sekiban.Web.Common;

/// <summary>
///     Aggregate information
/// </summary>
/// <param name="AggregateName"></param>
/// <param name="QueryInfo"></param>
/// <param name="Commands"></param>
public record SekibanAggregateInfo(string AggregateName, SekibanQueryInfo QueryInfo, List<SekibanCommandInfo> Commands);
