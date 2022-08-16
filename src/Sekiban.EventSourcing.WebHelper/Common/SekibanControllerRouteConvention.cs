using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
namespace Sekiban.EventSourcing.WebHelper.Common;

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
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            var commandType = controller.ControllerType.GenericTypeArguments[2];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_sekibanControllerOptions.ChangeCommandPrefix}/{aggregateType.Name.ToLower()}/{commandType.Name.ToLower()}")
                        {
                            Name = commandType.Name
                        })
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseCreateControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            var commandType = controller.ControllerType.GenericTypeArguments[2];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_sekibanControllerOptions.CreateCommandPrefix}/{aggregateType.Name.ToLower()}/{commandType.Name.ToLower()}"))
                    {
                        Name = commandType.Name
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseQueryGetControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name.ToLower()}/get"))
                    {
                        Name = aggregateType.Name + "Get"
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseSingleAggregateProjectionControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            var projectionType = controller.ControllerType.GenericTypeArguments[1];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name.ToLower()}/{projectionType.Name.ToLower()}"))
                    {
                        Name = aggregateType.Name + projectionType.Name
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseQueryListControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name.ToLower()}/list"))
                    {
                        Name = aggregateType.Name + "List"
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseMultipleAggregateProjectionControllerType.Name }
                .Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name.ToLower()}/get"))
                    {
                        Name = aggregateType.Name
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseMultipleAggregateListProjectionControllerType.Name }.Contains(
                controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name.ToLower()}/get"))
                    {
                        Name = aggregateType.Name + "Get"
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseMultipleAggregateListOnlyProjectionControllerType.Name }.Contains(
                controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name.ToLower()}/list"))
                    {
                        Name = aggregateType.Name + "ListOnly"
                    }
                });
        }
        if (controller.ControllerType.Name == _sekibanControllerOptions.BaseIndexControllerType.Name)
        {
            controller.ControllerName = "SekibanInfo";
            controller.Selectors.Add(
                new SelectorModel { AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(_sekibanControllerOptions.InfoPrefix)) });
        }
    }
}
