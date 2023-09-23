using BookBorrowing.Domain.Aggregates.Borrowers;
using BookBorrowing.Domain.Aggregates.Borrowers.Commands;
using Sekiban.Core.Dependency;
using System.Reflection;
namespace BookBorrowing.Domain;

public class BookBorrowingDependency : DomainDependencyDefinitionBase
{

    public override Assembly GetExecutingAssembly() => Assembly.GetExecutingAssembly();
    public override void Define()
    {
        AddAggregate<Borrower>()
            .AddCommandHandler<CreateBorrower, CreateBorrower.Handler>()
            .AddCommandHandler<ChangeBorrowerName, ChangeBorrowerName.Handler>();
    }
}
