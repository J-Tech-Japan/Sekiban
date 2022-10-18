using Microsoft.AspNetCore.Mvc;
using Sekiban.Addon.Web.Authorizations;
using Sekiban.Addon.Web.Common;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Document;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Shared;
using System.Reflection;
namespace Sekiban.Addon.Web.Controllers;

[Produces("application/json")]
[ApiController]
public class SekibanApiListController<T> : ControllerBase
{
    private readonly IDocumentRepository _documentRepository;
    private readonly ISekibanControllerItems _sekibanControllerItems;
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    private readonly IServiceProvider _serviceProvider;
    private readonly IUpdateNotice _updateNotice;
    public SekibanApiListController(
        SekibanControllerOptions sekibanControllerOptions,
        ISekibanControllerItems sekibanControllerItems,
        IDocumentRepository documentRepository,
        IServiceProvider serviceProvider,
        IUpdateNotice updateNotice)
    {
        _sekibanControllerOptions = sekibanControllerOptions;
        _sekibanControllerItems = sekibanControllerItems;
        _documentRepository = documentRepository;
        _serviceProvider = serviceProvider;
        _updateNotice = updateNotice;
    }

    [HttpGet]
    [Route("aggregates", Name = "SekibanAggregates")]
    public virtual async Task<ActionResult<List<SekibanAggregateInfo>>> AggregateInfoAsync()
    {
        if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                AuthorizeMethodType.AggregateInfo,
                this,
                typeof(T),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied) { return Unauthorized(); }

        await Task.CompletedTask;
        var list = new List<SekibanAggregateInfo>();
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
            if (aggregateContentsType is null) { continue; }
            if (aggregateType is null) { continue; }
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
                    if (aggregateType?.Name != interfaceType?.GenericTypeArguments[0].Name) { continue; }
                    var commandType = interfaceType?.GenericTypeArguments[1];
                    var responseType = typeof(AggregateCommandExecutorResponse);
                    if (commandType == null) { continue; }
                    if (aggregateType is null) { continue; }
                    aggregateInfo.commands.Add(
                        new SekibanCommandInfo
                        {
                            Url = $"/{_sekibanControllerOptions.CreateCommandPrefix}/{aggregateType.Name}/{commandType.Name}",
                            JsonBodyType = commandType.Name,
                            Method = "POST",
                            SampleBodyObject = Activator.CreateInstance(commandType)!,
                            SampleResponseObject = Activator.CreateInstance(responseType)!,
                            IsCreateEvent = true
                        });
                }
            }
            foreach (var (serviceType, implementationType) in _sekibanControllerItems.SekibanCommands)
            {
                var interfaceType = serviceType;

                if (interfaceType?.Name != typeof(IChangeAggregateCommandHandler<,>).Name) { continue; }
                if (aggregateType?.Name != interfaceType?.GenericTypeArguments[0].Name) { continue; }
                var commandType = interfaceType?.GenericTypeArguments[1];
                var responseType = typeof(AggregateCommandExecutorResponse);
                if (commandType == null) { continue; }
                if (aggregateContentsType == null) { continue; }
                if (aggregateType is null) { continue; }
                aggregateInfo.commands.Add(
                    new SekibanCommandInfo
                    {
                        Url = $"/{_sekibanControllerOptions.ChangeCommandPrefix}/{aggregateType.Name}/{commandType.Name}",
                        JsonBodyType = commandType.Name,
                        Method = "PATCH",
                        IsCreateEvent = false,
                        SampleBodyObject = Activator.CreateInstance(commandType)!,
                        SampleResponseObject = Activator.CreateInstance(responseType)!
                    });
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
            if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                    AuthorizeMethodType.EventHistory,
                    this,
                    aggregateType,
                    null,
                    null,
                    HttpContext,
                    _serviceProvider) ==
                AuthorizeResultType.Denied) { return Unauthorized(); }
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
            if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                    AuthorizeMethodType.CommandHistory,
                    this,
                    aggregateType,
                    null,
                    null,
                    HttpContext,
                    _serviceProvider) ==
                AuthorizeResultType.Denied) { return Unauthorized(); }
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
    [HttpPost]
    [Route("updatemarker/{aggregateName}/{id}", Name = "UpdateAggregateId")]
    public virtual async Task<IActionResult> UpdateMakerAsync(
        string aggregateName,
        Guid id,
        string sortableUniqueId,
        UpdatedLocationType locationType = UpdatedLocationType.ExternalFunction)
    {
        await Task.CompletedTask;
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            if (!string.Equals(aggregateName, aggregateType.Name, StringComparison.CurrentCultureIgnoreCase)) { continue; }
            if (_sekibanControllerOptions.AuthorizeDefinitionCollection.CheckAuthorization(
                    AuthorizeMethodType.SendUpdateMarker,
                    this,
                    aggregateType,
                    null,
                    null,
                    HttpContext,
                    _serviceProvider) ==
                AuthorizeResultType.Denied) { return Unauthorized(); }
            _updateNotice.SendUpdate(aggregateName, id, sortableUniqueId, locationType);
            return Ok(sortableUniqueId);
        }
        return Problem("Aggregate name not exists");
    }
}
