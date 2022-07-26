using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
namespace Sekiban.WebHelper.Common;

public class SekibanControllerRouteConvention : IControllerModelConvention
{
    [ApiExplorerSettings]
    public void Apply(ControllerModel controller)
    {
        if (controller.ControllerType.IsGenericType)
        {
            var generic1 = controller.ControllerType.GenericTypeArguments[0];
            var generic3 = controller.ControllerType.GenericTypeArguments[2];
            controller.ApiExplorer.IsVisible = false;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(new RouteAttribute($"{generic1.Name.ToLower()}/{generic3.Name.ToLower()}"))
                });
        }
    }
}
