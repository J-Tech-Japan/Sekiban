using Sekiban.Dcb;
using Sekiban.Dcb.Common;

namespace SekibanDcbOrleans.ApiService;

/// <summary>
///     Rejects malformed shared query parameters with 400 before they reach a query:
///     <c>pageNumber</c>/<c>pageSize</c> must be 1 or more, and <c>waitForSortableUniqueId</c> must be a
///     sortable unique id (otherwise the query would wait for an id that never arrives and time out).
/// </summary>
public sealed class QueryParameterValidationFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var errors = Validate(context.HttpContext.Request.Query);
        return errors.Count == 0 ? next(context) : ValueTask.FromResult<object?>(Results.ValidationProblem(errors));
    }

    public static Dictionary<string, string[]> Validate(IQueryCollection query)
    {
        var errors = new Dictionary<string, string[]>();
        foreach (var name in new[] { "pageNumber", "pageSize" })
        {
            var raw = query[name].ToString();
            if (raw.Length > 0 && (!int.TryParse(raw, out var value) || value < 1))
            {
                errors[name] = [$"{name} must be an integer of 1 or more."];
            }
        }

        var waitFor = query["waitForSortableUniqueId"].ToString();
        if (waitFor.Length > 0 && !SortableUniqueId.TryParse(waitFor, out _))
        {
            errors["waitForSortableUniqueId"] =
                ["waitForSortableUniqueId must be the 30-digit sortableUniqueId returned by a command."];
        }

        return errors;
    }
}

/// <summary>
///     Development projection routes take a projector name from the query string. An unknown name would
///     activate a projection grain for a projector that does not exist, which then fails on every
///     activation; answer 404 instead.
/// </summary>
public sealed class KnownProjectorFilter : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var name = context.HttpContext.Request.Query["name"].ToString();
        var domainTypes = context.HttpContext.RequestServices.GetRequiredService<DcbDomainTypes>();
        return IsRegistered(domainTypes, name)
            ? next(context)
            : ValueTask.FromResult<object?>(Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Projector Not Found",
                detail: $"Projector '{name}' is not registered."));
    }

    public static bool IsRegistered(DcbDomainTypes domainTypes, string? name) =>
        !string.IsNullOrEmpty(name) &&
        domainTypes.MultiProjectorTypes.GetAllProjectorNames().Contains(name, StringComparer.Ordinal);
}
