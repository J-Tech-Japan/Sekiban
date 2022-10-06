using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.EventSourcing.Queries.QueryModels;
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
                _sekibanControllerOptions.BaseAggregateQueryControllerType.MakeGenericType(aggregateType, aggregateContentsType).GetTypeInfo());
        }
        foreach (var projectionType in _sekibanControllerItems.SingleAggregateProjections)
        {
            var baseType = projectionType?.BaseType?.GetGenericTypeDefinition();
            if (baseType != typeof(SingleAggregateProjectionBase<,,>)) { continue; }
            var tAggregateContents = projectionType?.BaseType?.GenericTypeArguments[2];
            if (tAggregateContents is null) { continue; }
            var projection = (dynamic?)Activator.CreateInstance(projectionType!);
            if (projection == null) { continue; }

            var aggregateType = projection.OriginalAggregateType();
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseSingleAggregateProjectionControllerType
                    .MakeGenericType((Type)aggregateType, projectionType!, tAggregateContents)
                    .GetTypeInfo());
        }
        // foreach (var projectionType in _sekibanControllerItems.MultipleAggregatesProjections)
        // {
        //     var baseType = projectionType?.BaseType;
        //     if (baseType?.GetGenericTypeDefinition() != typeof(MultipleAggregateProjectionBase<>)) { continue; }
        //     var tAggregateContents = baseType.GenericTypeArguments[0];
        //     feature.Controllers.Add(
        //         _sekibanControllerOptions.BaseMultipleAggregateProjectionControllerType.MakeGenericType(projectionType!, tAggregateContents)
        //             .GetTypeInfo());
        // }
        foreach (var projectionType in _sekibanControllerItems.AggregateListQueryFilters)
        {
            var baseType = projectionType.GetInterfaces()
                ?.FirstOrDefault(m => m.Name.Contains(typeof(IAggregateListQueryFilterDefinition<,,,>).Name));
            if (baseType is null) { continue; }
            var tAggregate = baseType.GenericTypeArguments[0];
            var tAggregateContents = baseType.GenericTypeArguments[1];
            var tQueryParam = baseType.GenericTypeArguments[2];
            var tQueryResult = baseType.GenericTypeArguments[3];
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseAggregateListQueryFilterControllerType.MakeGenericType(
                        tAggregate,
                        tAggregateContents,
                        projectionType!,
                        tQueryParam,
                        tQueryResult)
                    .GetTypeInfo());
        }
        foreach (var projectionType in _sekibanControllerItems.AggregateQueryFilters)
        {
            var baseType = projectionType.GetInterfaces()?.FirstOrDefault(m => m.Name.Contains(typeof(IAggregateQueryFilterDefinition<,,,>).Name));
            if (baseType is null) { continue; }
            var tAggregate = baseType.GenericTypeArguments[0];
            var tAggregateContents = baseType.GenericTypeArguments[1];
            var tQueryParam = baseType.GenericTypeArguments[2];
            var tQueryResult = baseType.GenericTypeArguments[3];
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseAggregateQueryFilterControllerType.MakeGenericType(
                        tAggregate,
                        tAggregateContents,
                        projectionType!,
                        tQueryParam,
                        tQueryResult)
                    .GetTypeInfo());
        }
        foreach (var queryFilterType in _sekibanControllerItems.SingleAggregateProjectionListQueryFilters)
        {
            var baseType = queryFilterType.GetInterfaces()
                ?.FirstOrDefault(m => m.Name.Contains(typeof(ISingleAggregateProjectionListQueryFilterDefinition<,,,,>).Name));
            if (baseType is null) { continue; }
            var tAggregate = baseType.GenericTypeArguments[0];
            var tProjectionType = baseType.GenericTypeArguments[1];
            var tProjectionContentsType = baseType.GenericTypeArguments[2];
            var tQueryParam = baseType.GenericTypeArguments[3];
            var tQueryResult = baseType.GenericTypeArguments[4];
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseSingleAggregateProjectionListQueryFilterControllerType.MakeGenericType(
                        tAggregate,
                        tProjectionType,
                        tProjectionContentsType,
                        queryFilterType!,
                        tQueryParam,
                        tQueryResult)
                    .GetTypeInfo());
        }
        foreach (var queryFilterType in _sekibanControllerItems.SingleAggregateProjectionQueryFilters)
        {
            var baseType = queryFilterType.GetInterfaces()
                ?.FirstOrDefault(m => m.Name.Contains(typeof(ISingleAggregateProjectionQueryFilterDefinition<,,,,>).Name));
            if (baseType is null) { continue; }
            var tAggregate = baseType.GenericTypeArguments[0];
            var tProjectionType = baseType.GenericTypeArguments[1];
            var tProjectionContentsType = baseType.GenericTypeArguments[2];
            var tQueryParam = baseType.GenericTypeArguments[3];
            var tQueryResult = baseType.GenericTypeArguments[4];
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseSingleAggregateProjectionQueryFilterControllerType.MakeGenericType(
                        tAggregate,
                        tProjectionType,
                        tProjectionContentsType,
                        queryFilterType!,
                        tQueryParam,
                        tQueryResult)
                    .GetTypeInfo());
        }
        foreach (var queryFilterType in _sekibanControllerItems.ProjectionListQueryFilters)
        {
            var baseType = queryFilterType.GetInterfaces()
                ?.FirstOrDefault(m => m.Name.Contains(typeof(IProjectionListQueryFilterDefinition<,,,>).Name));
            if (baseType is null) { continue; }
            var tProjectionType = baseType.GenericTypeArguments[0];
            var tProjectionContentsType = baseType.GenericTypeArguments[1];
            var tQueryParam = baseType.GenericTypeArguments[2];
            var tQueryResult = baseType.GenericTypeArguments[3];
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseProjectionListQueryFilterControllerType.MakeGenericType(
                        tProjectionType,
                        tProjectionContentsType,
                        queryFilterType!,
                        tQueryParam,
                        tQueryResult)
                    .GetTypeInfo());
        }
        foreach (var queryFilterType in _sekibanControllerItems.ProjectionQueryFilters)
        {
            var baseType = queryFilterType.GetInterfaces()?.FirstOrDefault(m => m.Name.Contains(typeof(IProjectionQueryFilterDefinition<,,,>).Name));
            if (baseType is null) { continue; }
            var tProjectionType = baseType.GenericTypeArguments[0];
            var tProjectionContentsType = baseType.GenericTypeArguments[1];
            var tQueryParam = baseType.GenericTypeArguments[2];
            var tQueryResult = baseType.GenericTypeArguments[3];
            feature.Controllers.Add(
                _sekibanControllerOptions.BaseProjectionQueryFilterControllerType.MakeGenericType(
                        tProjectionType,
                        tProjectionContentsType,
                        queryFilterType!,
                        tQueryParam,
                        tQueryResult)
                    .GetTypeInfo());
        }
        feature.Controllers.Add(_sekibanControllerOptions.BaseIndexControllerType.MakeGenericType(typeof(object)).GetTypeInfo());
    }
}
