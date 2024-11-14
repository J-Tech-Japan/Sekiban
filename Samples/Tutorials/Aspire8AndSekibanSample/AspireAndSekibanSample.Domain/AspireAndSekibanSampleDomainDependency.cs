using AspireAndSekibanSample.Domain.Aggregates.AccountUsers;
using Sekiban.Core.Dependency;
using System.Reflection;
namespace AspireAndSekibanSample.Domain;

public class AspireAndSekibanSampleDomainDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    public override void Define()
    {
    }
}
