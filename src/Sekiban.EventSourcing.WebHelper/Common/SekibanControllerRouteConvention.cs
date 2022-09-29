using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
namespace Sekiban.EventSourcing.WebHelper.Common;

public class SekibanControllerRouteConvention : IControllerModelConvention
{
    private readonly SekibanControllerOptions _sekibanControllerOptions;

    public SekibanControllerRouteConvention(SekibanControllerOptions sekibanControllerOptions)
    {
        _sekibanControllerOptions = sekibanControllerOptions;
    }

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
            new List<string> { _sekibanControllerOptions.BaseAggregateQueryControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name.ToLower()}")
                        {
                            Name = aggregateType.Name + "Query"
                        })
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseSingleAggregateProjectionControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            var projectionType = controller.ControllerType.GenericTypeArguments[1];
            controller.ControllerName = projectionType.Name;
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
            new List<string> { _sekibanControllerOptions.BaseMultipleAggregateProjectionControllerType.Name }
                .Contains(controller.ControllerType.Name))
        {
            var projectionType = controller.ControllerType.GenericTypeArguments[0];
            controller.ControllerName = projectionType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{projectionType.Name.ToLower()}/get"))
                    {
                        Name = projectionType.Name
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseAggregateListQueryFilterControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            var queryFilterType = controller.ControllerType.GenericTypeArguments[2];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name.ToLower()}/{queryFilterType.Name.ToLower()}"))
                    {
                        Name = aggregateType.Name + queryFilterType.Name
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseSingleAggregateProjectionListQueryFilterControllerType.Name }.Contains(
                controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            var projectionType = controller.ControllerType.GenericTypeArguments[1];
            var queryFilterType = controller.ControllerType.GenericTypeArguments[2];
            controller.ControllerName = projectionType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_sekibanControllerOptions.QueryPrefix}/{aggregateType.Name.ToLower()}/{projectionType.Name.ToLower()}/{queryFilterType.Name.ToLower()}"))
                    {
                        Name = aggregateType.Name + projectionType.Name + queryFilterType.Name
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseProjectionListQueryFilterControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var projectionType = controller.ControllerType.GenericTypeArguments[0];
            var queryFilterType = controller.ControllerType.GenericTypeArguments[2];
            controller.ControllerName = projectionType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{projectionType.Name.ToLower()}/{queryFilterType.Name}"))
                    {
                        Name = projectionType.Name + queryFilterType.Name
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _sekibanControllerOptions.BaseProjectionQueryFilterControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var projectionType = controller.ControllerType.GenericTypeArguments[0];
            var queryFilterType = controller.ControllerType.GenericTypeArguments[2];
            controller.ControllerName = projectionType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_sekibanControllerOptions.QueryPrefix}/{projectionType.Name.ToLower()}/{queryFilterType.Name}"))
                    {
                        Name = projectionType.Name + queryFilterType.Name
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
