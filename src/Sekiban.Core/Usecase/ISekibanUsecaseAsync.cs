using ResultBoxes;
namespace Sekiban.Core.Usecase;

public interface ISekibanUsecaseAsync<in TIn, TOut> where TIn : class, ISekibanUsecaseAsync<TIn, TOut>, IEquatable<TIn>
    where TOut : notnull
{
    public static abstract Task<ResultBox<TOut>> ExecuteAsync(TIn input, ISekibanUsecaseContext context);
}
