using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using System.Reflection;
namespace Sekiban.EventSourcing.WebHelper.Common;

public class SekibanControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly ISekibanControllerItems _sekibanControllerItems;
    private readonly SekibanControllerOptions _sekibanControllerOptions;
    public SekibanControllerFeatureProvider(ISekibanControllerItems sekibanControllerItems, SekibanControllerOptions sekibanControllerOptions)
    {
        _sekibanControllerItems = sekibanControllerItems;
        _sekibanControllerOptions = sekibanControllerOptions;
    }

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var (serviceType, implementationType) in _sekibanControllerItems.SekibanCommands)
        {
            var interfaceType = serviceType;

            if (interfaceType?.Name == typeof(ICreateAggregateCommandHandler<,>).Name)
            {
                var aggregateType = interfaceType?.GenericTypeArguments[0];
                var commandType = interfaceType?.GenericTypeArguments[1];
                var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
                if (aggregateType is null || commandType is null || aggregateContentsType is null) { continue; }
                feature.Controllers.Add(
                    _sekibanControllerOptions.BaseCreateControllerType.MakeGenericType(aggregateType, aggregateContentsType, commandType)
                        .GetTypeInfo());
            }
            if (interfaceType?.Name == typeof(IChangeAggregateCommandHandler<,>).Name)
            {
                var aggregateType = interfaceType?.GenericTypeArguments[0];
                var commandType = interfaceType?.GenericTypeArguments[1];
                var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
                if (aggregateType is null || commandType is null || aggregateContentsType is null) { continue; }
                feature.Controllers.Add(
                    _sekibanControllerOptions.BaseChangeControllerType.MakeGenericType(aggregateType, aggregateContentsType, commandType)
                        .GetTypeInfo());
            }
        }
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
            if (aggregateType is null || aggregateContentsType is null) { continue; }
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseQueryGetControllerType.MakeGenericType(aggregateType, aggregateContentsType).GetTypeInfo());
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseQueryListControllerType.MakeGenericType(aggregateType, aggregateContentsType).GetTypeInfo());
        }
        foreach (var projectionType in _sekibanControllerItems.SingleAggregateProjections)
        {
            var baseType = projectionType?.BaseType?.GetGenericTypeDefinition();
            if (baseType != typeof(SingleAggregateProjectionBase<>)) { continue; }
            var projection = (dynamic?)Activator.CreateInstance(projectionType!);
            if (projection == null) { continue; }

            var aggregateType = projection.OriginalAggregateType();
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseSingleAggregateProjectionControllerType.MakeGenericType((Type)aggregateType, projectionType!)
                    .GetTypeInfo());
        }
        foreach (var projectionType in _sekibanControllerItems.MultipleAggregatesProjections)
        {
            var baseType = projectionType?.BaseType?.GetGenericTypeDefinition();
            if (baseType != typeof(MultipleAggregateProjectionBase<>)) { continue; }
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseMultipleAggregateProjectionControllerType.MakeGenericType(projectionType!).GetTypeInfo());
        }
        foreach (var projectionType in _sekibanControllerItems.MultipleAggregatesListProjections)
        {
            var baseType = projectionType?.BaseType?.GetGenericTypeDefinition();
            if (baseType != typeof(MultipleAggregateListProjectionBase<,>)) { continue; }
            var recordType = projectionType?.BaseType?.GenericTypeArguments[1];
            if (recordType == null) { continue; }
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseMultipleAggregateListProjectionControllerType.MakeGenericType(projectionType!, recordType)
                    .GetTypeInfo());
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseMultipleAggregateListOnlyProjectionControllerType.MakeGenericType(projectionType!, recordType)
                    .GetTypeInfo());
        }
        feature.Controllers.Add(_sekibanControllerOptions.BaseIndexControllerType.MakeGenericType(typeof(object)).GetTypeInfo());
    }
}
