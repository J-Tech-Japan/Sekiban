using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.WebHelper.Controllers;
using Sekiban.WebHelper.Controllers.Bases;
using System.Reflection;
namespace Sekiban.WebHelper.Common;

public class SekibanControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly ISekibanControllerItems _sekibanControllerItems;
    public SekibanControllerFeatureProvider(ISekibanControllerItems sekibanControllerItems) =>
        _sekibanControllerItems = sekibanControllerItems;

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
                if (aggregateType == null || commandType == null || aggregateContentsType == null) { continue; }
                feature.Controllers.Add(
                    typeof(BaseCreateCommandController<,,>).MakeGenericType(aggregateType, aggregateContentsType, commandType).GetTypeInfo());
            }
            if (interfaceType?.Name == typeof(IChangeAggregateCommandHandler<,>).Name)
            {
                var aggregateType = interfaceType?.GenericTypeArguments[0];
                var commandType = interfaceType?.GenericTypeArguments[1];
                var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
                if (aggregateType == null || commandType == null || aggregateContentsType == null) { continue; }
                feature.Controllers.Add(
                    typeof(BaseChangeCommandController<,,>).MakeGenericType(aggregateType, aggregateContentsType, commandType).GetTypeInfo());
            }
        }
        foreach (var aggregateType in _sekibanControllerItems.SekibanAggregates)
        {
            var aggregateContentsType = aggregateType?.BaseType?.GenericTypeArguments[0];
            if (aggregateType == null || aggregateContentsType == null) { continue; }
            feature.Controllers.Add(typeof(BaseQueryController<,>).MakeGenericType(aggregateType, aggregateContentsType).GetTypeInfo());
        }
        feature.Controllers.Add(typeof(SekibanApiListController<>).MakeGenericType(typeof(object)).GetTypeInfo());
    }
}
