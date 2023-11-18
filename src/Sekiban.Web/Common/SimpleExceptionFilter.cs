using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Sekiban.Core.Exceptions;
using System.Net;
namespace Sekiban.Web.Common;

public class SimpleExceptionFilter : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        context.HttpContext.Response.StatusCode = GetStatusCode(context);
        context.HttpContext.Response.ContentType = "application/json";
        context.Result = GetJsonResult(context);
        base.OnException(context);
    }
    public int GetStatusCode(ExceptionContext context) =>
        context.Exception switch
        {
            SekibanValidationErrorsException => (int)HttpStatusCode.BadRequest,
            ISekibanException => (int)HttpStatusCode.BadRequest,
            _ => (int)HttpStatusCode.InternalServerError
        };

    public JsonResult GetJsonResult(ExceptionContext context) =>
        context.Exception switch
        {
            ISekibanException sekibanException => new JsonResult(
                SekibanExceptionApiResponse.Create(sekibanException, context.HttpContext?.Request?.Path ?? string.Empty)),
            _ => new JsonResult(
                SekibanExceptionApiResponse.CreateFromException(context.Exception, context.HttpContext?.Request?.Path ?? string.Empty))
        };
}
