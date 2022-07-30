using Sekiban.EventSourcing.WebHelper.Controllers;
using Sekiban.EventSourcing.WebHelper.Controllers.Bases;
namespace Sekiban.EventSourcing.WebHelper.Common;

public class SekibanControllerOptions
{
    public Type BaseChangeControllerType { get; set; } = typeof(BaseChangeCommandController<,,>);

    public Type BaseCreateControllerType { get; set; } = typeof(BaseCreateCommandController<,,>);
    public Type BaseIndexControllerType { get; set; } = typeof(SekibanApiListController<>);
    public Type BaseQueryGetControllerType { get; set; } = typeof(BaseQueryGetController<,>);
    public Type BaseQueryListControllerType { get; set; } = typeof(BaseQueryListController<,>);
    public string CreateCommandPrefix { get; set; } = "api/command";
    public string ChangeCommandPrefix { get; set; } = "api/command";
    public string QueryPrefix { get; set; } = "api/query";
    public string InfoPrefix { get; set; } = "api/info";
}
