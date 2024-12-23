using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.AspNetCore.Usecase;
using Sekiban.Core.Command;

namespace Sekiban.Core.AspNetCore.Command;

public class CommandExecutor
{
    public static Func<TCommon, ISekibanExecutor, Task<IResult>> CreateSimpleCommandExecutor<TCommon>()
        where TCommon : class, ICommandCommon =>
        ([FromBody] input, [FromServices] executor) => executor.ExecuteCommand(input).ToResults();

    public static Func<TCommon, ISekibanExecutor, Task<IResult>> CreateSimpleCommandExecutorWithErrorHandler<TCommon>(
        Func<Exception, IResult> errorHandler) where TCommon : class, ICommandCommon =>
        ([FromBody] input, [FromServices] executor) => executor.ExecuteCommand(input).ToResults();
}
