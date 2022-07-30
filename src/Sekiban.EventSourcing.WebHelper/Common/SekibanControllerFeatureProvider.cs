using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Sekiban.EventSourcing.AggregateCommands;
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
        feature.Controllers.Add(_sekibanControllerOptions.BaseIndexControllerType.MakeGenericType(typeof(object)).GetTypeInfo());
    }
}
