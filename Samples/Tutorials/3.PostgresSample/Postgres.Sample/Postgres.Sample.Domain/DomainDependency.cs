using Postgres.Sample.Domain.Aggregates.Players;
using Sekiban.Core.Dependency;
using System.Reflection;
namespace Postgres.Sample.Domain;

public class DomainDependency : DomainDependencyDefinitionBase
{

    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    public override void Define()
    {
    }
}
