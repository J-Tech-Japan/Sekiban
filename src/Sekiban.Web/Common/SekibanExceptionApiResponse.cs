using Sekiban.Core.Exceptions;
namespace Sekiban.Web.Common;

public record SekibanExceptionApiResponse(string Message, string InnerExceptionMessage, string Source, string StackTrace, string Path)
{
    public static SekibanExceptionApiResponse Create(ISekibanException exception, string path) =>
        exception switch
        {
            Exception e => CreateFromException(e, path),
            _ => new SekibanExceptionApiResponse("Unknown Error", string.Empty, string.Empty, string.Empty, string.Empty)
        };
    public static SekibanExceptionApiResponse CreateFromException(Exception exception, string path) =>
        new(
            exception.Message,
            exception.InnerException?.Message ?? string.Empty,
            exception.Source ?? exception.InnerException?.Source ?? string.Empty,
            exception.StackTrace ?? exception.InnerException?.StackTrace ?? string.Empty,
            path);
}