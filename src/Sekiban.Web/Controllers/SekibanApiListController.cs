using Microsoft.AspNetCore.Mvc;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Types;
using Sekiban.Web.Authorizations;
using Sekiban.Web.Common;
using Sekiban.Web.Dependency;
using System.Reflection;
namespace Sekiban.Web.Controllers;

/// <summary>
///     Sekiban api list controller
/// </summary>
/// <typeparam name="T"></typeparam>
[Produces("application/json")]
[ApiController]
public class SekibanApiListController<T>(
    IWebDependencyDefinition webDependencyDefinition,
    IDocumentPersistentRepository documentRepository,
    IServiceProvider serviceProvider,
    IUpdateNotice updateNotice) : ControllerBase
{

    [HttpGet]
    [Route("aggregates", Name = "SekibanAggregates")]
    public virtual async Task<ActionResult<List<SekibanAggregateInfo>>> AggregateInfoAsync()
    {
        if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                AuthorizeMethodType.AggregateInfo,
                this,
                typeof(T),
                null,
                null,
                HttpContext,
                serviceProvider) ==
            AuthorizeResultType.Denied)
        {
            return Unauthorized();
        }

        await Task.CompletedTask;
        var list = new List<SekibanAggregateInfo>();
        foreach (var aggregateType in webDependencyDefinition.GetAggregatePayloadTypes())
        {
            var stateResponseType = typeof(AggregateState<>).MakeGenericType(aggregateType).GetTypeInfo();
            var aggregateInfo = new SekibanAggregateInfo(
                aggregateType.Name,
                new SekibanQueryInfo
                {
                    GetUrl = $"/{webDependencyDefinition.Options.QueryPrefix}/{aggregateType.Name}/get",
                    GetEventsUrl = $"/{webDependencyDefinition.Options.InfoPrefix}/events/{aggregateType.Name}/{{id}}",
                    GetCommandsUrl = $"/{webDependencyDefinition.Options.InfoPrefix}/commands/{aggregateType.Name}/{{id}}",
                    Method = "GET",
                    SampleResponseObject = Activator.CreateInstance(stateResponseType)!
                },
                []);
            list.Add(aggregateInfo);
            foreach (var (_, implementationType) in webDependencyDefinition.GetCommandDependencies())
            {
                if (implementationType != null && implementationType.IsCommandHandlerType())
                {
                    if (aggregateType != implementationType.GetAggregatePayloadTypeFromCommandHandlerType())
                    {
                        continue;
                    }
                    var commandType = implementationType.GetCommandTypeFromCommandHandlerType();
                    var responseType = typeof(CommandExecutorResponse);
                    aggregateInfo.Commands.Add(
                        new SekibanCommandInfo
                        {
                            Url = $"/{webDependencyDefinition.Options.CreateCommandPrefix}/{aggregateType.Name}/{commandType.Name}",
                            JsonBodyType = commandType.Name,
                            Method = "POST",
                            SampleBodyObject = commandType.CreateDefaultInstance(),
                            SampleResponseObject = responseType.CreateDefaultInstance()
                        });
                }
            }
        }

        return Ok(list);
    }


    [HttpGet]
    [Route("events/{aggregateName}/{id}", Name = "SekibanEvents")]
    public virtual async Task<ActionResult<IEnumerable<dynamic>>> EventsAsync(
        string aggregateName,
        Guid id,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey)
    {
        foreach (var aggregateType in webDependencyDefinition.GetAggregatePayloadTypes())
        {
            if (!string.Equals(aggregateName, aggregateType.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                    AuthorizeMethodType.EventHistory,
                    this,
                    aggregateType,
                    null,
                    null,
                    HttpContext,
                    serviceProvider) ==
                AuthorizeResultType.Denied)
            {
                return Unauthorized();
            }
            var events = new List<dynamic>();
            await documentRepository.GetAllEventsForAggregateIdAsync(
                id,
                aggregateType,
                PartitionKeyGenerator.ForEvent(id, aggregateType, rootPartitionKey),
                null,
                rootPartitionKey,
                eventObjects => { events.AddRange(eventObjects); });
            return Ok(events);
        }

        return Problem("AddAggregate name not exists");
    }

    [HttpGet]
    [Route("commands/{aggregateName}/{id}", Name = "SekibanCommands")]
    public virtual async Task<ActionResult<IEnumerable<dynamic>>> CommandsAsync(string aggregateName, Guid id, string rootPartitionKey)
    {
        foreach (var aggregateType in webDependencyDefinition.GetAggregatePayloadTypes())
        {
            if (!string.Equals(aggregateName, aggregateType.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                    AuthorizeMethodType.CommandHistory,
                    this,
                    aggregateType,
                    null,
                    null,
                    HttpContext,
                    serviceProvider) ==
                AuthorizeResultType.Denied)
            {
                return Unauthorized();
            }
            var events = new List<dynamic>();
            await documentRepository.GetAllCommandStringsForAggregateIdAsync(
                id,
                aggregateType,
                null,
                rootPartitionKey,
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
    public virtual async Task<ActionResult<IEnumerable<SnapshotDocument>>> SnapshotsAsync(
        string aggregateName,
        Guid aggregateId,
        string rootPartitionKey)
    {
        foreach (var aggregatePayloadType in webDependencyDefinition.GetAggregatePayloadTypes())
        {
            if (!string.Equals(aggregateName, aggregatePayloadType.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                    AuthorizeMethodType.SnapshotHistory,
                    this,
                    aggregatePayloadType,
                    null,
                    null,
                    HttpContext,
                    serviceProvider) ==
                AuthorizeResultType.Denied)
            {
                return Unauthorized();
            }
            var snapshots = await documentRepository.GetSnapshotsForAggregateAsync(
                aggregateId,
                aggregatePayloadType,
                aggregatePayloadType,
                rootPartitionKey);
            return Ok(snapshots);
        }

        return Problem("AddAggregate name not exists");
    }

    [HttpGet]
    [Route("snapshots/{aggregateName}/{aggregateId}/{snapshotId}", Name = "SekibanSnapshotsWithSnapshotId")]
    public virtual async Task<ActionResult<SnapshotDocument>> SnapshotsAsync(
        string aggregateName,
        Guid aggregateId,
        Guid snapshotId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey)
    {
        foreach (var aggregatePayloadType in webDependencyDefinition.GetAggregatePayloadTypes())
        {
            if (!string.Equals(aggregateName, aggregatePayloadType.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                    AuthorizeMethodType.SnapshotHistory,
                    this,
                    aggregatePayloadType,
                    null,
                    null,
                    HttpContext,
                    serviceProvider) ==
                AuthorizeResultType.Denied)
            {
                return Unauthorized();
            }
            var snapshots = await documentRepository.GetSnapshotsForAggregateAsync(
                aggregateId,
                aggregatePayloadType,
                aggregatePayloadType,
                rootPartitionKey);
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
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        UpdatedLocationType locationType = UpdatedLocationType.ExternalFunction)
    {
        await Task.CompletedTask;
        foreach (var aggregateType in webDependencyDefinition.GetAggregatePayloadTypes())
        {
            if (!string.Equals(aggregateName, aggregateType.Name, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }
            if (webDependencyDefinition.AuthorizationDefinitions.CheckAuthorization(
                    AuthorizeMethodType.SendUpdateMarker,
                    this,
                    aggregateType,
                    null,
                    null,
                    HttpContext,
                    serviceProvider) ==
                AuthorizeResultType.Denied)
            {
                return Unauthorized();
            }
            updateNotice.SendUpdate(rootPartitionKey, aggregateName, id, sortableUniqueId, locationType);
            return Ok(sortableUniqueId);
        }

        return Problem("AddAggregate name not exists");
    }
}
