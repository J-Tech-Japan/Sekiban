namespace Sekiban.Core.Query.QueryModel;

public interface INextQueryCommon<TOutput> : INextQueryCommon, INextQueryGeneral<TOutput> where TOutput : notnull;
public interface INextQueryCommon;
