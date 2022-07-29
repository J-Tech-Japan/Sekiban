using Microsoft.AspNetCore.Mvc;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Documents;
using Sekiban.EventSourcing.Partitions;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.WebHelper.Controllers.Bases;

[ApiController]
public class BaseQueryController<TAggregate, TAggregateContents> : ControllerBase
    where TAggregate : TransferableAggregateBase<TAggregateContents>, new() where TAggregateContents : IAggregateContents, new()
{
    private readonly IDocumentRepository _documentRepository;
    private readonly IMultipleAggregateProjectionService _multipleAggregateProjectionService;
    private readonly ISingleAggregateService _singleAggregateService;
    public BaseQueryController(
        IMultipleAggregateProjectionService multipleAggregateProjectionService,
        ISingleAggregateService singleAggregateService,
        IDocumentRepository documentRepository)
    {
        _multipleAggregateProjectionService = multipleAggregateProjectionService;
        _singleAggregateService = singleAggregateService;
        _documentRepository = documentRepository;
    }

    [HttpGet]
    [Route("{id}")]
    public async Task<ActionResult<AggregateDto<TAggregateContents>>> GetAsync(Guid id, int? toVersion = null) =>
        Ok(await _singleAggregateService.GetAggregateAsync<TAggregate, TAggregateContents>(id, toVersion));
    [HttpGet]
    [Route("{id}/events")]
    public async Task<ActionResult<IEnumerable<dynamic>>> GetEventsAsync(Guid id)
    {
        var events = new List<dynamic>();
        await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
            id,
            typeof(TAggregate),
            PartitionKeyGenerator.ForAggregateEvent(id, typeof(TAggregate)),
            null,
            eventObjects =>
            {
                events.AddRange(eventObjects);
            });
        return Ok(events);
    }
    [HttpGet]
    [Route("list")]
    public async Task<ActionResult<IEnumerable<AggregateDto<TAggregateContents>>>> ListAsync() =>
        Ok(await _multipleAggregateProjectionService.GetAggregateList<TAggregate, TAggregateContents>());
}
