using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Sekiban.Core.Types;
using Sekiban.Web.Dependency;
using System.Reflection;
namespace Sekiban.Web.Common;

public class SekibanControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly IWebDependencyDefinition _webDependencyDefinition;

    public SekibanControllerFeatureProvider(IWebDependencyDefinition webDependencyDefinition) => _webDependencyDefinition = webDependencyDefinition;

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var (_, implementationType) in _webDependencyDefinition.GetCommandDependencies())
        {
            if (implementationType != null && implementationType.IsCommandHandlerType())
            {
                feature.Controllers.Add(
                    _webDependencyDefinition.Options.BaseControllerType.MakeGenericType(
                            implementationType.GetAggregatePayloadTypeFromCommandHandlerType(),
                            implementationType.GetCommandTypeFromCommandHandlerType())
                        .GetTypeInfo());
            }
        }
        foreach (var aggregateType in _webDependencyDefinition.GetAggregatePayloadTypes())
        {
            feature.Controllers.Add(_webDependencyDefinition.Options.BaseGetAggregateControllerType.MakeGenericType(aggregateType).GetTypeInfo());
        }
        foreach (var aggregateType in _webDependencyDefinition.GetAggregatePayloadSubtypes())
        {
            feature.Controllers.Add(_webDependencyDefinition.Options.BaseGetAggregateControllerType.MakeGenericType(aggregateType).GetTypeInfo());
        }
        foreach (var projectionType in _webDependencyDefinition.GetSingleProjectionTypes())
        {
            if (projectionType.IsSingleProjectionPayloadType())
            {
                feature.Controllers.Add(
                    _webDependencyDefinition.Options.BaseSingleProjectionControllerType.MakeGenericType(projectionType).GetTypeInfo());
            }
        }
        foreach (var projectionType in _webDependencyDefinition.GetAggregateListQueryTypes()
            .Concat(_webDependencyDefinition.GetSimpleAggregateListQueryTypes()))
        {
            if (!projectionType.IsAggregateListQueryType())
            {
                continue;
            }
            feature.Controllers.Add(
                _webDependencyDefinition.Options.BaseAggregateListQueryControllerType.MakeGenericType(
                        projectionType.GetAggregateTypeFromAggregateListQueryType(),
                        projectionType,
                        projectionType.GetParamTypeFromAggregateListQueryType(),
                        projectionType.GetResponseTypeFromAggregateListQueryType())
                    .GetTypeInfo());
        }

        foreach (var projectionType in _webDependencyDefinition.GetAggregateQueryTypes())
        {
            if (!projectionType.IsAggregateQueryType())
            {
                continue;
            }
            feature.Controllers.Add(
                _webDependencyDefinition.Options.BaseAggregateQueryControllerType.MakeGenericType(
                        projectionType.GetAggregateTypeFromAggregateQueryType(),
                        projectionType,
                        projectionType.GetParamTypeFromAggregateQueryType(),
                        projectionType.GetResponseTypeFromAggregateQueryType())
                    .GetTypeInfo());
        }

        foreach (var queryType in _webDependencyDefinition.GetSingleProjectionListQueryTypes())
        {
            if (!queryType.IsSingleProjectionListQueryType())
            {
                continue;
            }
            feature.Controllers.Add(
                _webDependencyDefinition.Options.BaseSingleProjectionListQueryControllerType.MakeGenericType(
                        queryType.GetSingleProjectionTypeFromSingleProjectionListQueryType(),
                        queryType,
                        queryType.GetParamTypeFromSingleProjectionListQueryType(),
                        queryType.GetResponseTypeFromSingleProjectionListQueryType())
                    .GetTypeInfo());
        }

        foreach (var queryType in _webDependencyDefinition.GetSingleProjectionQueryTypes())
        {
            if (!queryType.IsSingleProjectionQueryType())
            {
                continue;
            }
            feature.Controllers.Add(
                _webDependencyDefinition.Options.BaseSingleProjectionQueryControllerType.MakeGenericType(
                        queryType.GetSingleProjectionTypeFromSingleProjectionQueryType(),
                        queryType,
                        queryType.GetParamTypeFromSingleProjectionQueryType(),
                        queryType.GetResponseTypeFromSingleProjectionQueryType())
                    .GetTypeInfo());
        }

        foreach (var queryType in _webDependencyDefinition.GetMultiProjectionListQueryTypes())
        {
            if (!queryType.IsMultiProjectionListQueryType())
            {
                continue;
            }
            feature.Controllers.Add(
                _webDependencyDefinition.Options.BaseMultiProjectionListQueryControllerType.MakeGenericType(
                        queryType.GetMultiProjectionTypeFromMultiProjectionListQueryType(),
                        queryType,
                        queryType.GetParamTypeFromMultiProjectionListQueryType(),
                        queryType.GetResponseTypeFromMultiProjectionListQueryType())
                    .GetTypeInfo());
        }

        foreach (var queryType in _webDependencyDefinition.GetMultiProjectionQueryTypes())
        {
            if (!queryType.IsMultiProjectionQueryType())
            {
                continue;
            }
            feature.Controllers.Add(
                _webDependencyDefinition.Options.BaseMultiProjectionQueryControllerType.MakeGenericType(
                        queryType.GetMultiProjectionTypeFromMultiProjectionQueryType(),
                        queryType,
                        queryType.GetParamTypeFromMultiProjectionQueryType(),
                        queryType.GetResponseTypeFromMultiProjectionQueryType())
                    .GetTypeInfo());
        }

        foreach (var queryType in _webDependencyDefinition.GetGeneralListQueryTypes())
        {
            if (!queryType.IsGeneralListQueryType())
            {
                continue;
            }
            feature.Controllers.Add(
                _webDependencyDefinition.Options.BaseGeneralListQueryControllerType.MakeGenericType(
                        queryType,
                        queryType.GetParamTypeFromGeneralListQueryType(),
                        queryType.GetResponseTypeFromGeneralListQueryType())
                    .GetTypeInfo());
        }

        foreach (var queryType in _webDependencyDefinition.GetGeneralQueryTypes())
        {
            if (!queryType.IsGeneralQueryType())
            {
                continue;
            }
            feature.Controllers.Add(
                _webDependencyDefinition.Options.BaseGeneralQueryControllerType.MakeGenericType(
                        queryType,
                        queryType.GetParamTypeFromGeneralQueryType(),
                        queryType.GetResponseTypeFromGeneralQueryType())
                    .GetTypeInfo());
        }

        feature.Controllers.Add(_webDependencyDefinition.Options.BaseIndexControllerType.MakeGenericType(typeof(object)).GetTypeInfo());
    }
}
