using Sekiban.EventSourcing.WebHelper.Authorizations;
using Sekiban.EventSourcing.WebHelper.Authorizations.Definitions;
using Sekiban.EventSourcing.WebHelper.Controllers;
using Sekiban.EventSourcing.WebHelper.Controllers.Bases;
namespace Sekiban.EventSourcing.WebHelper.Common;

public class SekibanControllerOptions
{
    public Type BaseChangeControllerType { get; set; } = typeof(BaseChangeCommandController<,,>);

    public Type BaseCreateControllerType { get; set; } = typeof(BaseCreateCommandController<,,>);
    public Type BaseIndexControllerType { get; set; } = typeof(SekibanApiListController<>);
    public Type BaseAggregateQueryControllerType { get; set; } = typeof(BaseAggregateQueryController<,>);
    public Type BaseSingleAggregateProjectionControllerType { get; set; } = typeof(BaseSingleAggregateProjectionController<,,>);
    // public Type BaseMultipleAggregateProjectionControllerType { get; set; } = typeof(BaseMultipleAggregateProjectionController<,>);
    public Type BaseAggregateListQueryFilterControllerType { get; set; } = typeof(BaseAggregateListQueryFilterController<,,,,>);
    public Type BaseAggregateQueryFilterControllerType { get; set; } = typeof(BaseAggregateListQueryFilterController<,,,,>);
    public Type BaseSingleAggregateProjectionListQueryFilterControllerType { get; set; }
        = typeof(BaseSingleAggregateProjectionListQueryFilterController<,,,,,>);
    public Type BaseSingleAggregateProjectionQueryFilterControllerType { get; set; }
        = typeof(BaseSingleAggregateProjectionListQueryFilterController<,,,,,>);
    public Type BaseProjectionQueryFilterControllerType { get; set; } = typeof(BaseProjectionQueryFilterController<,,,,>);
    public Type BaseProjectionListQueryFilterControllerType { get; set; } = typeof(BaseProjectionListQueryFilterController<,,,,>);
    public string CreateCommandPrefix { get; set; } = "api/command";
    public string ChangeCommandPrefix { get; set; } = "api/command";
    public string QueryPrefix { get; set; } = "api/query";
    public string InfoPrefix { get; set; } = "api/info";
    public IAuthorizeDefinitionCollection AuthorizeDefinitionCollection { get; set; } = new AuthorizeDefinitionCollection(new Allow<AllMethod>());
}
