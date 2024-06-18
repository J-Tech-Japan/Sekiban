namespace Sekiban.Core.Query.QueryModel;

public interface INextQueryCommon<TOutput> : INextQueryGeneral<TOutput> where TOutput : notnull;