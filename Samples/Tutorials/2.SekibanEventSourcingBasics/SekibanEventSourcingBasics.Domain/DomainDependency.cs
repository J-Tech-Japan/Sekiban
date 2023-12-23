using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Sekiban.Core.Dependency;
using SekibanEventSourcingBasics.Domain.Aggregates.UserPoints;
using SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Commands;

namespace SekibanEventSourcingBasics.Domain;

public class DomainDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();

    public override void Define()
    {
        AddAggregate<UserPoint>()
            .AddCommandHandler<CreateUserPoint, CreateUserPoint.Handler>()
            .AddCommandHandler<ChangeUserPointName, ChangeUserPointName.Handler>();
    }
}