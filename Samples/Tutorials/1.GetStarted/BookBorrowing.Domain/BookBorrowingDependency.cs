using Sekiban.Core.Dependency;
using System.Reflection;
namespace BookBorrowing.Domain;

public class BookBorrowingDependency : DomainDependencyDefinitionBase
{
    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    public override void Define()
    {
    }
}
