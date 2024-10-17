using ResultBoxes;
namespace Sekiban.Core.Usecase;

public class SekibanUsecaseExecutor(ISekibanUsecaseContext context) : ISekibanUsecaseExecutor
{
    public Task<ResultBox<TOut>> Execute<TIn, TOut>(ISekibanUsecaseAsync<TIn, TOut> usecaseAsync)
        where TIn : class, ISekibanUsecaseAsync<TIn, TOut>, IEquatable<TIn> where TOut : notnull =>
        ResultBox.CheckNull(usecaseAsync as TIn).Conveyor(arg => TIn.ExecuteAsync(arg, context));
    public ResultBox<TOut> Execute<TIn, TOut>(ISekibanUsecase<TIn, TOut> usecaseAsync)
        where TIn : class, ISekibanUsecase<TIn, TOut>, IEquatable<TIn> where TOut : notnull =>
        ResultBox.CheckNull(usecaseAsync as TIn).Conveyor(arg => TIn.Execute(arg, context));
}
