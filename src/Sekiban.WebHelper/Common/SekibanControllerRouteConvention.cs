using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
namespace Sekiban.WebHelper.Common;

public class SekibanControllerRouteConvention : IControllerModelConvention
{
    private readonly SekibanControllerOptions _sekibanControllerOptions;

    public SekibanControllerRouteConvention(SekibanControllerOptions sekibanControllerOptions) =>
        _sekibanControllerOptions = sekibanControllerOptions;

    [ApiExplorerSettings]
    public void Apply(ControllerModel controller)
    {
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseChangeControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var generic1 = controller.ControllerType.GenericTypeArguments[0];
            var generic3 = controller.ControllerType.GenericTypeArguments[2];
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_sekibanControllerOptions.ChangeCommandPrefix}/{generic1.Name.ToLower()}/{generic3.Name.ToLower()}"))
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseCreateControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var generic1 = controller.ControllerType.GenericTypeArguments[0];
            var generic3 = controller.ControllerType.GenericTypeArguments[2];
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_sekibanControllerOptions.CreateCommandPrefix}/{generic1.Name.ToLower()}/{generic3.Name.ToLower()}"))
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseQueryControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var genericAggregate = controller.ControllerType.GenericTypeArguments[0];
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{genericAggregate.Name.ToLower()}"))
                });
        }
        if (controller.ControllerType.Name == _sekibanControllerOptions.BaseIndexControllerType.Name)
        {
            controller.Selectors.Add(
                new SelectorModel { AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(_sekibanControllerOptions.IndexPrefix)) });
        }
    }
}
