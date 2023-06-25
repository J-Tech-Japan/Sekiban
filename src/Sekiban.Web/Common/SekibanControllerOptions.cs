using Sekiban.Web.Controllers;
using Sekiban.Web.Controllers.Bases;
namespace Sekiban.Web.Common;

public class SekibanControllerOptions
{
    public Type BaseControllerType { get; set; } = typeof(BaseCommandController<,>);
    public Type BaseIndexControllerType { get; set; } = typeof(SekibanApiListController<>);
    public Type BaseGetAggregateControllerType { get; set; } = typeof(BaseGetAggregateController<>);
    public Type BaseSingleProjectionControllerType { get; set; } = typeof(BaseSingleProjectionController<>);
    public Type BaseAggregateListQueryControllerType { get; set; } = typeof(BaseAggregateListQueryController<,,,>);
    public Type BaseAggregateQueryControllerType { get; set; } = typeof(BaseAggregateQueryController<,,,>);

    public Type BaseSingleProjectionListQueryControllerType { get; set; } = typeof(BaseSingleProjectionListQueryController<,,,>);

    public Type BaseSingleProjectionQueryControllerType { get; set; } = typeof(BaseSingleProjectionQueryController<,,,>);

    public Type BaseMultiProjectionQueryControllerType { get; set; } = typeof(BaseMultiProjectionQueryController<,,,>);

    public Type BaseMultiProjectionListQueryControllerType { get; set; } = typeof(BaseMultiProjectionListQueryController<,,,>);
    public Type BaseGeneralQueryControllerType { get; set; } = typeof(BaseGeneralQueryController<,,>);

    public Type BaseGeneralListQueryControllerType { get; set; } = typeof(BaseGeneralListQueryController<,,>);

    public string CreateCommandPrefix { get; set; } = "api/command";
    public string ChangeCommandPrefix { get; set; } = "api/command";
    public string QueryPrefix { get; set; } = "api/query";
    public string InfoPrefix { get; set; } = "api/info";
}
