using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Partitions;
using Sekiban.EventSourcing.Shared;
using Sekiban.EventSourcing.WebHelper.Common;
using System.Reflection;
namespace Sekiban.EventSourcing.WebHelper.Controllers;

[ApiController]
public class SekibanApiListController<T> : ControllerBase
{
    private readonly IDocumentRepository _documentRepository;
    private readonly ISekibanControllerItems _sekibanControllerItems;
    private readonly SekibanControllerOptions _sekibanControllerOptions;

    public SekibanApiListController(
        SekibanControllerOptions sekibanControllerOptions,
        ISekibanControllerItems sekibanControllerItems,
        IDocumentRepository documentRepository)
    {
        _sekibanControllerOptions = sekibanControllerOptions;
        _sekibanControllerItems = sekibanControllerItems;
        _documentRepository = documentRepository;
    }

    [HttpGet]
    [Route("createCommands")]
    public async Task<ActionResult<List<SekibanCommandInfo>>> CreateCommandListAsync()
    {
        await Task.CompletedTask;
        var list = new List<SekibanCommandInfo>();
        foreach (var (serviceType, implementationType) in _sekibanControllerItems.SekibanCommands)
        {
            var interfaceType = serviceType;

            if (interfaceType?.Name == typeof(ICreateAggregateCommandHandler<,>).Name)
            {
                var aggregateType = interfaceType?.GenericTypeArguments[0];
                var commandType = interfaceType?.GenericTypeArguments[1];
                var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
                var responseType = typeof(AggregateCommandExecutorResponse<,>);
                var actualResponseType = responseType.MakeGenericType(aggregateContentsType, commandType).GetTypeInfo();
                if (aggregateType is null || commandType is null || aggregateContentsType is null) { continue; }
                list.Add(
                    new SekibanCommandInfo
                    {
                        Url = $"/{_sekibanControllerOptions.CreateCommandPrefix}/{aggregateType.Name}/{commandType.Name}",
                        JsonBodyType = commandType.Name,
                        Method = "POST",
                        AggregateType = aggregateType.Name,
                        SampleBodyObject = Activator.CreateInstance(commandType)!,
                        SampleResponseObject = Activator.CreateInstance(actualResponseType)!
                    });
            }
        }
        return Ok(list);
    }

    [HttpGet]
    [Route("changeCommands")]
    public async Task<ActionResult<List<SekibanCommandInfo>>> ChangeCommandListAsync()
    {
        await Task.CompletedTask;
        var list = new List<SekibanCommandInfo>();
        foreach (var (serviceType, implementationType) in _sekibanControllerItems.SekibanCommands)
        {
            var interfaceType = serviceType;

            if (interfaceType?.Name == typeof(IChangeAggregateCommandHandler<,>).Name)
            {
                var aggregateType = interfaceType?.GenericTypeArguments[0];
                var commandType = interfaceType?.GenericTypeArguments[1];
                var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
                var responseType = typeof(AggregateCommandExecutorResponse<,>);
                var actualResponseType = responseType.MakeGenericType(aggregateContentsType, commandType).GetTypeInfo();
                if (aggregateType is null || commandType is null || aggregateContentsType is null) { continue; }
                list.Add(
                    new SekibanCommandInfo
                    {
                        Url = $"/{_sekibanControllerOptions.ChangeCommandPrefix}/{aggregateType.Name}/{commandType.Name}",
                        JsonBodyType = commandType.Name,
                        Method = "PATCH",
                        AggregateType = aggregateType.Name,
                        SampleBodyObject = Activator.CreateInstance(commandType)!,
                        SampleResponseObject = Activator.CreateInstance(actualResponseType)!
                    });
            }
        }
        return Ok(list);
    }

    [HttpGet]
    [Route("getQueries")]
    public async Task<ActionResult<List<SekibanQueryInfo>>> GetQueryListAsync()
    {
        await Task.CompletedTask;
        var list = new List<SekibanQueryInfo>();
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
            var responseType = typeof(AggregateDto<>).MakeGenericType(aggregateContentsType).GetTypeInfo();
            if (aggregateType is null || aggregateContentsType is null) { continue; }
            list.Add(
                new SekibanQueryInfo
                {
                    Url = $"/{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name}/{{id}}",
                    Method = "GET",
                    AggregateType = aggregateType.Name,
                    SampleResponseObject = Activator.CreateInstance(responseType)!
                });
        }
        return Ok(list);
    }
    [HttpGet]
    [Route("listQueries")]
    public async Task<ActionResult<List<SekibanQueryInfo>>> GetListQueryListAsync()
    {
        await Task.CompletedTask;
        var list = new List<SekibanQueryInfo>();
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
            var responseType = typeof(AggregateDto<>).MakeGenericType(aggregateContentsType).GetTypeInfo();
            if (aggregateType is null || aggregateContentsType is null) { continue; }
            list.Add(
                new SekibanQueryInfo
                {
                    Url = $"/{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name}",
                    Method = "GET",
                    AggregateType = aggregateType.Name,
                    SampleResponseObject = new List<dynamic> { Activator.CreateInstance(responseType)! }
                });
        }
        return Ok(list);
    }

    [HttpGet]
    [Route("{aggregateName}/{id}/events")]
    public async Task<ActionResult<IEnumerable<dynamic>>> GetEventsAsync(string aggregateName, Guid id)
    {
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            if (!string.Equals(aggregateName, aggregateType.Name, StringComparison.CurrentCultureIgnoreCase)) { continue; }
            var events = new List<dynamic>();
            await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
                id,
                aggregateType,
                PartitionKeyGenerator.ForAggregateEvent(id, aggregateType),
                null,
                eventObjects =>
                {
                    events.AddRange(eventObjects);
                });
            return Ok(events);
        }
        return Problem("Aggregate name not exists");
    }
    [HttpGet]
    [Route("{aggregateName}/{id}/commands")]
    public async Task<ActionResult<IEnumerable<dynamic>>> GetCommandsAsync(string aggregateName, Guid id)
    {
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            if (!string.Equals(aggregateName, aggregateType.Name, StringComparison.CurrentCultureIgnoreCase)) { continue; }
            var events = new List<dynamic>();
            await _documentRepository.GetAllAggregateCommandStringsForAggregateIdAsync(
                id,
                aggregateType,
                null,
                eventObjects =>
                {
                    events.AddRange(eventObjects.Select(m => SekibanJsonHelper.Deserialize(m, typeof(object))!));
                });
            return Ok(events);
        }
        return Problem("Aggregate name not exists");
    }
}
