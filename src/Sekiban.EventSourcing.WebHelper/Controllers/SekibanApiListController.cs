using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.WebHelper.Common;
namespace Sekiban.EventSourcing.WebHelper.Controllers;

[ApiController]
public class SekibanApiListController<T> : ControllerBase
{
    private readonly ISekibanControllerItems _sekibanControllerItems;
    private readonly SekibanControllerOptions _sekibanControllerOptions;

    public SekibanApiListController(SekibanControllerOptions sekibanControllerOptions, ISekibanControllerItems sekibanControllerItems)
    {
        _sekibanControllerOptions = sekibanControllerOptions;
        _sekibanControllerItems = sekibanControllerItems;
    }

    [HttpGet]
    [Route("createCommands")]
    public async Task<ActionResult<List<SekibanURLInfo>>> CreateCommandListAsync()
    {
        await Task.CompletedTask;
        var list = new List<SekibanURLInfo>();
        foreach (var (serviceType, implementationType) in _sekibanControllerItems.SekibanCommands)
        {
            var interfaceType = serviceType;

            if (interfaceType?.Name == typeof(ICreateAggregateCommandHandler<,>).Name)
            {
                var aggregateType = interfaceType?.GenericTypeArguments[0];
                var commandType = interfaceType?.GenericTypeArguments[1];
                var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
                if (aggregateType == null || commandType == null || aggregateContentsType == null) { continue; }
                list.Add(
                    new SekibanURLInfo
                    {
                        Url = $"/{_sekibanControllerOptions.CreateCommandPrefix}/{aggregateType.Name}/{commandType.Name}",
                        JsonBodyType = commandType.Name,
                        Method = "POST"
                    });
            }
        }
        return Ok(list);
    }

    [HttpGet]
    [Route("changeCommands")]
    public async Task<ActionResult<List<SekibanURLInfo>>> ChangeCommandListAsync()
    {
        await Task.CompletedTask;
        var list = new List<SekibanURLInfo>();
        foreach (var (serviceType, implementationType) in _sekibanControllerItems.SekibanCommands)
        {
            var interfaceType = serviceType;

            if (interfaceType?.Name == typeof(IChangeAggregateCommandHandler<,>).Name)
            {
                var aggregateType = interfaceType?.GenericTypeArguments[0];
                var commandType = interfaceType?.GenericTypeArguments[1];
                var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
                if (aggregateType == null || commandType == null || aggregateContentsType == null) { continue; }
                list.Add(
                    new SekibanURLInfo
                    {
                        Url = $"/{_sekibanControllerOptions.ChangeCommandPrefix}/{aggregateType.Name}/{commandType.Name}",
                        JsonBodyType = commandType.Name,
                        Method = "PATCH"
                    });
            }
        }
        return Ok(list);
    }

    [HttpGet]
    [Route("getQueries")]
    public async Task<ActionResult<List<SekibanURLInfo>>> GetQueryListAsync()
    {
        await Task.CompletedTask;
        var list = new List<SekibanURLInfo>();
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
            if (aggregateType == null || aggregateContentsType == null) { continue; }
            list.Add(new SekibanURLInfo { Url = $"/{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name}/{{id}}", Method = "GET" });
        }
        return Ok(list);
    }
    [HttpGet]
    [Route("listQueries")]
    public async Task<ActionResult<List<SekibanURLInfo>>> GetListQueryListAsync()
    {
        await Task.CompletedTask;
        var list = new List<SekibanURLInfo>();
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
            if (aggregateType == null || aggregateContentsType == null) { continue; }
            list.Add(new SekibanURLInfo { Url = $"/{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name}", Method = "GET" });
        }
        return Ok(list);
    }
}
