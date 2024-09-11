namespace Sekiban.Core.Query.QueryModel;

public interface INextQueryCommonOutput<TOutput> : INextQueryCommon, INextQueryGeneral<TOutput> where TOutput : notnull;
