namespace Sekiban.Core.Query.QueryModel;

public interface INextQueryGeneral : IQueryPartitionKeyCommon
{
}
public interface INextQueryGeneral<TOutput> : INextQueryGeneral where TOutput : notnull;
