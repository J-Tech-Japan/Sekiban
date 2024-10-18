using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ResultBoxes;
namespace Sekiban.Core.Usecase;

public static class SekibanUsecase
{
    public static IResult ToResults<TResult>(this ResultBox<TResult> resultBox) where TResult : notnull =>
        Results.Ok(resultBox.UnwrapBox());
    public static async Task<IResult> ToResults<TResult>(this Task<ResultBox<TResult>> resultBox)
        where TResult : notnull => Results.Ok(await resultBox.UnwrapBox());



    public static Func<TUsecase, ISekibanExecutor, Task<IResult>> CreateSimpleExecutorAsync<TUsecase, TOut>()
        where TUsecase : class, ISekibanUsecaseAsync<TUsecase, TOut>, IEquatable<TUsecase> where TOut : notnull =>
        ([FromBody] input, [FromServices] executor) => executor.ExecuteUsecase(input).ToResults();
    
    
    public static Func<TUsecase, ISekibanExecutor, IResult> CreateSimpleExecutor<TUsecase, TOut>()
        where TUsecase : class, ISekibanUsecase<TUsecase, TOut>, IEquatable<TUsecase> where TOut : notnull =>
        ([FromBody] input, [FromServices] executor) => executor.ExecuteUsecase(input).ToResults();




    public static Func<TIn, ISekibanExecutor, IResult> CreateExecutor<TIn, TOut>(
        ISekibanUsecase<TIn, TOut> usecase,
        Func<Exception, IResult> exceptionMatch) where TIn : class, ISekibanUsecase<TIn, TOut>, IEquatable<TIn>
        where TOut : notnull =>
        ([FromBody] input, [FromServices] executor) =>
            executor.ExecuteUsecase(input).Match(success => Results.Ok(success), exceptionMatch);

    public static Func<TIn, ISekibanExecutor, IResult> CreateExecutor<TIn, TOut>(
        ISekibanUsecase<TIn, TOut> usecase,
        Func<TOut, IResult> valueMatch,
        Func<Exception, IResult> exceptionMatch) where TIn : class, ISekibanUsecase<TIn, TOut>, IEquatable<TIn>
        where TOut : notnull =>
        ([FromBody] input, [FromServices] executor) => executor.ExecuteUsecase(input).Match(valueMatch, exceptionMatch);
}
