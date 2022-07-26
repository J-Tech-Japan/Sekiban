using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Sekiban.EventSourcing.AggregateCommands;
using Sekiban.WebHelper.Controllers.Bases;
using System.Reflection;
namespace Sekiban.WebHelper.Common;

public class SekibanControllerFeatureProvider : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly IEnumerable<(Type serviceType, Type? implementationType)> SekibanCommands;

    public SekibanControllerFeatureProvider(IEnumerable<(Type serviceType, Type? implementationType)> sekibanCommands) =>
        SekibanCommands = sekibanCommands;

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        foreach (var (serviceType, implementationType) in SekibanCommands)
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
        }
    }
}
