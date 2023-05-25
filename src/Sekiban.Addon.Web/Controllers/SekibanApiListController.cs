using Microsoft.AspNetCore.Mvc;
using Sekiban.Addon.Web.Authorizations;
using Sekiban.Addon.Web.Common;
using Sekiban.Addon.Web.Dependency;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Addon.Web.Controllers;

[Produces("application/json")]
[ApiController]
public class SekibanApiListController<T> : ControllerBase
{
    private readonly IDocumentPersistentRepository _documentRepository;
    private readonly IServiceProvider _serviceProvider;
    private readonly IUpdateNotice _updateNotice;
    private readonly IWebDependencyDefinition _webDependencyDefinition;

    public SekibanApiListController(
        IWebDependencyDefinition webDependencyDefinition,
        IDocumentPersistentRepository documentRepository,
        IServiceProvider serviceProvider,
        IUpdateNotice updateNotice)
    {
        _webDependencyDefinition = webDependencyDefinition;
        _documentRepository = documentRepository;
        _serviceProvider = serviceProvider;
        _updateNotice = updateNotice;
    }

    [HttpGet]
    [Route("aggregates", Name = "SekibanAggregates")]
    public virtual async Task<ActionResult<List<SekibanAggregateInfo>>> AggregateInfoAsync()
    {
        if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.AggregateInfo,
                this,
                typeof(T),
                null,
                null,
                HttpContext,
                _serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }

        await Task.CompletedTask;
        var list = new List<SekibanAggregateInfo>();
        foreach (var aggregateType in _webDependencyDefinition.GetAggregatePayloadTypes())
        {
            var stateResponseType = typeof(AggregateState<>).MakeGenericType(aggregateType).GetTypeInfo();
            var aggregateInfo = new SekibanAggregateInfo(
                aggregateType.Name,
                new SekibanQueryInfo
                {
                    GetUrl = $"/{_webDependencyDefinition.Options.QueryPrefix}/{aggregateType.Name}/get",
                    ListUrl = $"/{_webDependencyDefinition.Options.QueryPrefix}/{aggregateType.Name}/list",
                    GetEventsUrl = $"/{_webDependencyDefinition.Options.InfoPrefix}/events/{aggregateType.Name}/{{id}}",
                    GetCommandsUrl =
                        $"/{_webDependencyDefinition.Options.InfoPrefix}/commands/{aggregateType.Name}/{{id}}",
                    Method = "GET",
                    SampleResponseObject = Activator.CreateInstance(stateResponseType)!
                },
                new List<SekibanCommandInfo>());
            list.Add(aggregateInfo);
            foreach (var (serviceType, implementationType) in _webDependencyDefinition.GetCommandDependencies())
            {
                if (implementationType != null && implementationType.IsCommandHandlerType())
                {
                    if (aggregateType != implementationType.GetAggregatePayloadTypeFromCommandHandlerType())
                    {
                        continue;
                    }
                    var commandType = implementationType.GetCommandTypeFromCommandHandlerType();
                    var responseType = typeof(CommandExecutorResponse);
                    aggregateInfo.commands.Add(
                        new SekibanCommandInfo
                        {
                            Url =
                                $"/{_webDependencyDefinition.Options.CreateCommandPrefix}/{aggregateType.Name}/{commandType.Name}",
                            JsonBodyType = commandType.Name,
                            Method = "POST",
                            SampleBodyObject = Activator.CreateInstance(commandType)!,
                            SampleResponseObject = Activator.CreateInstance(responseType)!,
                            IsCreateEvent = true
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
        foreach (var aggregateType in _webDependencyDefinition.GetAggregatePayloadTypes())
        {
            if (!string.Equals(aggregateName, aggregateType.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                    AuthorizeMethodType.EventHistory,
                    this,
                    aggregateType,
                    null,
                    null,
                    HttpContext,
                    _serviceProvider) ==
                AuthorizeResultType.Denied)
            {
                return Unauthorized();
            }
            var events = new List<dynamic>();
            await _documentRepository.GetAllEventsForAggregateIdAsync(
                id,
                aggregateType,
                PartitionKeyGenerator.ForEvent(id, aggregateType),
                null,
                eventObjects => { events.AddRange(eventObjects); });
            return Ok(events);
        }

        return Problem("AddAggregate name not exists");
    }

    [HttpGet]
    [Route("commands/{aggregateName}/{id}", Name = "SekibanCommands")]
    public virtual async Task<ActionResult<IEnumerable<dynamic>>> CommandsAsync(string aggregateName, Guid id)
    {
        foreach (var aggregateType in _webDependencyDefinition.GetAggregatePayloadTypes())
        {
            if (!string.Equals(aggregateName, aggregateType.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                    AuthorizeMethodType.CommandHistory,
                    this,
                    aggregateType,
                    null,
                    null,
                    HttpContext,
                    _serviceProvider) ==
                AuthorizeResultType.Denied)
            {
                return Unauthorized();
            }
            var events = new List<dynamic>();
            await _documentRepository.GetAllCommandStringsForAggregateIdAsync(
                id,
                aggregateType,
                null,
                eventObjects =>
                {
                    events.AddRange(eventObjects.Select(m => SekibanJsonHelper.Deserialize(m, typeof(object))!));
                });
            return Ok(events);
        }

        return Problem("AddAggregate name not exists");
    }

    [HttpGet]
    [Route("snapshots/{aggregateName}/{aggregateId}", Name = "SekibanSnapshots")]
    public virtual async Task<ActionResult<IEnumerable<SnapshotDocument>>> SnapshotsAsync(string aggregateName, Guid aggregateId)
    {
        foreach (var aggregatePayloadType in _webDependencyDefinition.GetAggregatePayloadTypes())
        {
            if (!string.Equals(aggregateName, aggregatePayloadType.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                    AuthorizeMethodType.SnapshotHistory,
                    this,
                    aggregatePayloadType,
                    null,
                    null,
                    HttpContext,
                    _serviceProvider) ==
                AuthorizeResultType.Denied)
            {
                return Unauthorized();
            }
            var snapshots = await _documentRepository.GetSnapshotsForAggregateAsync(aggregateId, aggregatePayloadType, aggregatePayloadType);
            return Ok(snapshots);
        }

        return Problem("AddAggregate name not exists");
    }

    [HttpGet]
    [Route("snapshots/{aggregateName}/{aggregateId}/{snapshotId}", Name = "SekibanSnapshotsWithSnapshotId")]
    public virtual async Task<ActionResult<SnapshotDocument>> SnapshotsAsync(string aggregateName, Guid aggregateId, Guid snapshotId)
    {
        foreach (var aggregatePayloadType in _webDependencyDefinition.GetAggregatePayloadTypes())
        {
            if (!string.Equals(aggregateName, aggregatePayloadType.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                    AuthorizeMethodType.SnapshotHistory,
                    this,
                    aggregatePayloadType,
                    null,
                    null,
                    HttpContext,
                    _serviceProvider) ==
                AuthorizeResultType.Denied)
            {
                return Unauthorized();
            }
            var snapshots = await _documentRepository.GetSnapshotsForAggregateAsync(aggregateId, aggregatePayloadType, aggregatePayloadType);
            return Ok(snapshots.FirstOrDefault(m => m.Id == snapshotId));
        }

        return Problem("AddAggregate name not exists");
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
        foreach (var aggregateType in _webDependencyDefinition.GetAggregatePayloadTypes())
        {
            if (!string.Equals(aggregateName, aggregateType.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (_webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                    AuthorizeMethodType.SendUpdateMarker,
                    this,
                    aggregateType,
                    null,
                    null,
                    HttpContext,
                    _serviceProvider) ==
                AuthorizeResultType.Denied)
            {
                return Unauthorized();
            }
            _updateNotice.SendUpdate(aggregateName, id, sortableUniqueId, locationType);
            return Ok(sortableUniqueId);
        }

        return Problem("AddAggregate name not exists");
    }
}
