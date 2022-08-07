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
    [Route("aggregates", Name = "SekibanAggregates")]
    public virtual async Task<ActionResult<List<SekibanAggregateInfo>>> AggregateInfoAsync()
    {
        await Task.CompletedTask;
        var list = new List<SekibanAggregateInfo>();
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
            var dtoResponseType = typeof(AggregateDto<>).MakeGenericType(aggregateContentsType).GetTypeInfo();
            var aggregateInfo = new SekibanAggregateInfo(
                aggregateType.Name,
                new SekibanQueryInfo
                {
                    GetUrl = $"/{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name}/get",
                    ListUrl = $"/{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name}/list",
                    GetEventsUrl = $"/{_sekibanControllerOptions.InfoPrefix}/events/{aggregateType.Name}/{{id}}",
                    GetCommandsUrl = $"/{_sekibanControllerOptions.InfoPrefix}/commands/{aggregateType.Name}/{{id}}",
                    Method = "GET",
                    SampleResponseObject = Activator.CreateInstance(dtoResponseType)!
                },
                new List<SekibanCommandInfo>());
            list.Add(aggregateInfo);
            foreach (var (serviceType, implementationType) in _sekibanControllerItems.SekibanCommands)
            {
                var interfaceType = serviceType;

                if (interfaceType?.Name == typeof(ICreateAggregateCommandHandler<,>).Name)
                {
                    if (aggregateType.Name != interfaceType?.GenericTypeArguments[0].Name) { continue; }
                    var commandType = interfaceType?.GenericTypeArguments[1];
                    var responseType = typeof(AggregateCommandExecutorResponse<,>);
                    var actualResponseType = responseType.MakeGenericType(aggregateContentsType, commandType).GetTypeInfo();
                    if (aggregateType is null || commandType is null || aggregateContentsType is null) { continue; }
                    aggregateInfo.commands.Add(
                        new SekibanCommandInfo
                        {
                            Url = $"/{_sekibanControllerOptions.CreateCommandPrefix}/{aggregateType.Name}/{commandType.Name}",
                            JsonBodyType = commandType.Name,
                            Method = "POST",
                            SampleBodyObject = Activator.CreateInstance(commandType)!,
                            SampleResponseObject = Activator.CreateInstance(actualResponseType)!,
                            IsCreateEvent = true
                        });
                }
            }
            foreach (var (serviceType, implementationType) in _sekibanControllerItems.SekibanCommands)
            {
                var interfaceType = serviceType;

                if (interfaceType?.Name == typeof(IChangeAggregateCommandHandler<,>).Name)
                {
                    if (aggregateType.Name != interfaceType?.GenericTypeArguments[0].Name) { continue; }
                    var commandType = interfaceType?.GenericTypeArguments[1];
                    var responseType = typeof(AggregateCommandExecutorResponse<,>);
                    var actualResponseType = responseType.MakeGenericType(aggregateContentsType, commandType).GetTypeInfo();
                    if (aggregateType is null || commandType is null || aggregateContentsType is null) { continue; }
                    aggregateInfo.commands.Add(
                        new SekibanCommandInfo
                        {
                            Url = $"/{_sekibanControllerOptions.ChangeCommandPrefix}/{aggregateType.Name}/{commandType.Name}",
                            JsonBodyType = commandType.Name,
                            Method = "PATCH",
                            IsCreateEvent = false,
                            SampleBodyObject = Activator.CreateInstance(commandType)!,
                            SampleResponseObject = Activator.CreateInstance(actualResponseType)!
                        });
                }
            }
        }
        return Ok(list);
    }
    [HttpGet]
    [Route("events/{aggregateName}/{id}", Name = "SekibanEvents")]
    public virtual async Task<ActionResult<IEnumerable<dynamic>>> EventsAsync(string aggregateName, Guid id)
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
    [Route("commands/{aggregateName}/{id}", Name = "SekibanCommands")]
    public virtual async Task<ActionResult<IEnumerable<dynamic>>> CommandsAsync(string aggregateName, Guid id)
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
