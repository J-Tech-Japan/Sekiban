using Sekiban.WebHelper.Controllers;
using Sekiban.WebHelper.Controllers.Bases;
namespace Sekiban.WebHelper.Common;

public class SekibanControllerOptions
{
    public Type BaseChangeControllerType { get; set; } = typeof(BaseChangeCommandController<,,>);

    public Type BaseCreateControllerType { get; set; } = typeof(BaseCreateCommandController<,,>);
    public Type BaseIndexControllerType { get; set; } = typeof(SekibanApiListController<>);
    public Type BaseQueryControllerType { get; set; } = typeof(BaseQueryController<,>);
    public string CreateCommandPrefix { get; set; } = "api/command";
    public string ChangeCommandPrefix { get; set; } = "api/command";
    public string QueryPrefix { get; set; } = "api/query";
    public string IndexPrefix { get; set; } = "api";
}
