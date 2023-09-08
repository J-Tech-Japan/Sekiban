using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Types;
using Sekiban.Web.Dependency;
namespace Sekiban.Web.Common;

/// <summary>
///     set more controller route information.
/// </summary>
public class SekibanControllerRouteConvention : IControllerModelConvention
{
    private readonly IWebDependencyDefinition _webDependencyDefinition;

    public SekibanControllerRouteConvention(IWebDependencyDefinition webDependencyDefinition) => _webDependencyDefinition = webDependencyDefinition;

    [ApiExplorerSettings]
    public void Apply(ControllerModel controller)
    {
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            var commandType = controller.ControllerType.GenericTypeArguments[1];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_webDependencyDefinition.Options.ChangeCommandPrefix}/{aggregateType.Name.ToLower()}/{commandType.Name.ToLower()}")
                        {
                            Name = commandType.Name
                        })
                });
        }

        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseGetAggregateControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_webDependencyDefinition.Options.QueryPrefix}/{aggregateType.Name.ToLower()}")
                        {
                            Name = aggregateType.Name + "AddQuery"
                        })
                });
        }

        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseSingleProjectionControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var projectionPayloadType = controller.ControllerType.GenericTypeArguments[0];
            var originalType = projectionPayloadType.GetAggregatePayloadTypeFromSingleProjectionPayload() ??
                throw new SekibanTypeNotFoundException("Can not find original type of " + projectionPayloadType.Name);
            controller.ControllerName = originalType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_webDependencyDefinition.Options.QueryPrefix}/{originalType.Name.ToLower()}/{projectionPayloadType.Name.ToLower()}/"))
                    {
                        Name = projectionPayloadType.Name
                    }
                });
        }

        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseAggregateListQueryControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            var queryType = controller.ControllerType.GenericTypeArguments[1];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_webDependencyDefinition.Options.QueryPrefix}/{aggregateType.Name.ToLower()}/{queryType.Name.Replace("`", "").ToLower()}"))
                    {
                        Name = aggregateType.Name + queryType.Name
                    }
                });
        }

        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseAggregateQueryControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var aggregateType = controller.ControllerType.GenericTypeArguments[0];
            var queryType = controller.ControllerType.GenericTypeArguments[1];
            controller.ControllerName = aggregateType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_webDependencyDefinition.Options.QueryPrefix}/{aggregateType.Name.ToLower()}/{queryType.Name.Replace("`", "").ToLower()}"))
                    {
                        Name = aggregateType.Name + queryType.Name
                    }
                });
        }

        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseSingleProjectionListQueryControllerType.Name }.Contains(
                controller.ControllerType.Name))
        {
            var projectionType = controller.ControllerType.GenericTypeArguments[0] ??
                throw new SekibanTypeNotFoundException("Can not find projection type of " + controller.ControllerType.Name);
            var originalType = projectionType.GetAggregatePayloadTypeFromSingleProjectionPayload() ??
                throw new SekibanTypeNotFoundException("Can not find original type of " + projectionType.Name);
            var queryType = controller.ControllerType.GenericTypeArguments[1];
            controller.ControllerName = originalType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_webDependencyDefinition.Options.QueryPrefix}/{originalType.Name.ToLower()}/{projectionType.Name.ToLower()}/{queryType.Name.Replace("`", "").ToLower()}"))
                    {
                        Name = projectionType.Name + queryType.Name
                    }
                });
        }

        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseSingleProjectionQueryControllerType.Name }.Contains(
                controller.ControllerType.Name))
        {
            var projectionType = controller.ControllerType.GenericTypeArguments[0] ??
                throw new SekibanTypeNotFoundException("Can not find projection type of " + controller.ControllerType.Name);
            var originalType = projectionType.GetAggregatePayloadTypeFromSingleProjectionPayload() ??
                throw new SekibanTypeNotFoundException("Can not find original type of " + projectionType.Name);
            var queryType = controller.ControllerType.GenericTypeArguments[1];
            controller.ControllerName = originalType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_webDependencyDefinition.Options.QueryPrefix}/{originalType.Name.ToLower()}/{projectionType.Name.ToLower()}/{queryType.Name.Replace("`", "").ToLower()}"))
                    {
                        Name = projectionType.Name + queryType.Name
                    }
                });
        }

        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseMultiProjectionListQueryControllerType.Name }.Contains(
                controller.ControllerType.Name))
        {
            var projectionType = controller.ControllerType.GenericTypeArguments[0];
            var queryType = controller.ControllerType.GenericTypeArguments[1];
            controller.ControllerName = projectionType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_webDependencyDefinition.Options.QueryPrefix}/{projectionType.Name.ToLower()}/{queryType.Name.Replace("`", "").ToLower()}"))
                    {
                        Name = projectionType.Name + queryType.Name
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseGeneralListQueryControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var queryType = controller.ControllerType.GenericTypeArguments[0];
            controller.ControllerName = queryType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_webDependencyDefinition.Options.QueryPrefix}/{queryType.Name.Replace("`", "").ToLower()}"))
                    {
                        Name = queryType.Name
                    }
                });
        }

        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseMultiProjectionQueryControllerType.Name }
                .Contains(controller.ControllerType.Name))
        {
            var projectionType = controller.ControllerType.GenericTypeArguments[0];
            var queryType = controller.ControllerType.GenericTypeArguments[1];
            controller.ControllerName = projectionType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute(
                            $"{_webDependencyDefinition.Options.QueryPrefix}/{projectionType.Name.ToLower()}/{queryType.Name.Replace("`", "").ToLower()}"))
                    {
                        Name = projectionType.Name + queryType.Name
                    }
                });
        }
        if (controller.ControllerType.IsGenericType &&
            new List<string> { _webDependencyDefinition.Options.BaseGeneralQueryControllerType.Name }.Contains(controller.ControllerType.Name))
        {
            var queryType = controller.ControllerType.GenericTypeArguments[0];
            controller.ControllerName = queryType.Name;
            controller.Selectors.Add(
                new SelectorModel
                {
                    AttributeRouteModel = new AttributeRouteModel(
                        new RouteAttribute($"{_webDependencyDefinition.Options.QueryPrefix}/{queryType.Name.Replace("`", "").ToLower()}"))
                    {
                        Name = queryType.Name
                    }
                });
        }

        if (controller.ControllerType.Name == _webDependencyDefinition.Options.BaseIndexControllerType.Name)
        {
            controller.ControllerName = "SekibanInfo";
            controller.Selectors.Add(
                new SelectorModel { AttributeRouteModel = new AttributeRouteModel(new RouteAttribute(_webDependencyDefinition.Options.InfoPrefix)) });
        }
    }
}
