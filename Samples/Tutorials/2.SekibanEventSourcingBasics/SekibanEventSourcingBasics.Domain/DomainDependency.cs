using Sekiban.Core.Dependency;
using SekibanEventSourcingBasics.Domain.Aggregates.UserPoints;
using SekibanEventSourcingBasics.Domain.Aggregates.UserPoints.Commands;
using System.Reflection;
namespace SekibanEventSourcingBasics.Domain;

public class DomainDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();

    public override void Define()
    {
    }
}
