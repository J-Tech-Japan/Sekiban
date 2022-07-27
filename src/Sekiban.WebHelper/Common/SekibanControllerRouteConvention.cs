using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Sekiban.WebHelper.Controllers;
using Sekiban.WebHelper.Controllers.Bases;
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
            new List<string> { typeof(BaseChangeCommandController<,,>).Name }.Contains(controller.ControllerType.Name))
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
            new List<string> { typeof(BaseCreateCommandController<,,>).Name }.Contains(controller.ControllerType.Name))
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
            new List<string> { typeof(BaseQueryController<,>).Name }.Contains(controller.ControllerType.Name))
        {
            var genericAggregate = controller.ControllerType.GenericTypeArguments[0];
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{genericAggregate.Name.ToLower()}"))
                });
        }
        if (controller.ControllerType.Name == typeof(SekibanApiListController<>).Name ||
            controller.ControllerType.BaseType?.Name == typeof(SekibanApiListController<>).Name)
        {
            controller.Selectors.Add(
                new SelectorModel { AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(_sekibanControllerOptions.IndexPrefix)) });
        }
    }
}
