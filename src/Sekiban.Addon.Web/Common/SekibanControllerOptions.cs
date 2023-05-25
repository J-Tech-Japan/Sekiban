using Sekiban.Addon.Web.Controllers;
using Sekiban.Addon.Web.Controllers.Bases;
namespace Sekiban.Addon.Web.Common;

public class SekibanControllerOptions
{
    public Type BaseControllerType { get; set; } = typeof(BaseCommandController<,>);
    public Type BaseIndexControllerType { get; set; } = typeof(SekibanApiListController<>);
    public Type BaseGetAggregateControllerType { get; set; } = typeof(BaseGetAggregateController<>);
    public Type BaseSingleProjectionControllerType { get; set; } = typeof(BaseSingleProjectionController<>);
    public Type BaseAggregateListQueryControllerType { get; set; } = typeof(BaseAggregateListQueryController<,,,>);
    public Type BaseAggregateQueryControllerType { get; set; } = typeof(BaseAggregateQueryController<,,,>);

    public Type BaseSingleProjectionListQueryControllerType { get; set; }
        = typeof(BaseSingleProjectionListQueryController<,,,>);

    public Type BaseSingleProjectionQueryControllerType { get; set; }
        = typeof(BaseSingleProjectionQueryController<,,,>);

    public Type BaseMultiProjectionQueryControllerType { get; set; } = typeof(BaseMultiProjectionQueryController<,,,>);

    public Type BaseMultiProjectionListQueryControllerType { get; set; } =
        typeof(BaseMultiProjectionListQueryController<,,,>);

    public string CreateCommandPrefix { get; set; } = "api/command";
    public string ChangeCommandPrefix { get; set; } = "api/command";
    public string QueryPrefix { get; set; } = "api/query";
    public string InfoPrefix { get; set; } = "api/info";
}
