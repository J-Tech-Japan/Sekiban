namespace Sekiban.WebHelper.Controllers;
#if false
[ApiController]
[Route("api/query")]
public class SekibanQueryAccessController : ControllerBase
{
    private readonly List<Type> _aggregateTypes = new();
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly ISingleAggregateService _singleAggregateService;

    public SekibanQueryAccessController(
        ISingleAggregateService singleAggregateService,
        IMultipleAggregateProjectionService multipleAggregateProjectionService)
    {
        _singleAggregateService = singleAggregateService;
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
        _aggregateTypes.Add(typeof(Branch));
        _aggregateTypes.Add(typeof(Client));
        _aggregateTypes.Add(typeof(LoyaltyPoint));
    }

    [HttpGet]
    [Route("{aggregateName}/{aggregateId}")]
    public async Task<IActionResult> CreateCommandExecuterAsync(string aggregateName, Guid aggregateId, int? toVersion = null)
    {
        foreach (var aggregateType in _aggregateTypes)
        {
            if (aggregateName.Trim().ToLower() != aggregateType.Name.Trim().ToLower()) { continue; }
            var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
            if (aggregateContentsType == null) { return Problem("Aggregate does not have content"); }

            var createMethod = _singleAggregateService.GetType().GetMethod(nameof(ISingleAggregateService.GetAggregateDtoAsync));
            var methodInfo = createMethod?.MakeGenericMethod(aggregateType, aggregateContentsType);
            if (methodInfo == null) { return Problem("Aggregate Command can not execute."); }
            var responseTask = (Task?)methodInfo.Invoke(_singleAggregateService, new object?[] { aggregateId, toVersion });
            if (responseTask == null) { return Problem("Aggregate Command can not execute."); }
            await responseTask;
            var resultProperty = responseTask.GetType().GetProperty("Result");
            if (resultProperty == null) { return Problem("Aggregate Command can not execute."); }
            var result = resultProperty.GetValue(responseTask);

            return Ok(result);
        }

        await Task.CompletedTask;
        return Problem("Aggregate Command not found");
    }
    [HttpGet]
    [Route("{aggregateName}/list")]
    public async Task<IActionResult> ListAsync(string aggregateName)
    {
        foreach (var aggregateType in _aggregateTypes)
        {
            if (aggregateName.Trim().ToLower() != aggregateType.Name.Trim().ToLower()) { continue; }
            var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
            if (aggregateContentsType == null) { return Problem("Aggregate does not have content"); }

            var listMethod = _multipleAggregateProjectionService.GetType().GetMethod(nameof(IMultipleAggregateProjectionService.GetAggregateList));
            var methodInfo = listMethod?.MakeGenericMethod(aggregateType, aggregateContentsType);
            if (methodInfo == null) { return Problem("Aggregate Command can not execute."); }
            var responseTask = (Task?)methodInfo.Invoke(_multipleAggregateProjectionService, new object?[] { QueryListType.ActiveOnly });
            if (responseTask == null) { return Problem("Aggregate Command can not execute."); }
            await responseTask;
            var resultProperty = responseTask.GetType().GetProperty("Result");
            if (resultProperty == null) { return Problem("Aggregate Command can not execute."); }
            var result = resultProperty.GetValue(responseTask);

            return Ok(result);
        }

        await Task.CompletedTask;
        return Problem("Aggregate Command not found");
    }
}
#endif
