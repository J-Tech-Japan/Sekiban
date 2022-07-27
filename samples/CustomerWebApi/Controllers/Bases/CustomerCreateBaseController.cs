using Microsoft.AspNetCore.Authorization;
using Sekiban.EventSourcing.WebHelper.Controllers.Bases;
namespace CustomerWebApi.Controllers.Bases;

[Authorize]
public class
    CustomerCreateBaseController<TAggregate, TAggregateContents, TAggregateCommand> : BaseCreateCommandController<TAggregate, TAggregateContents,
        TAggregateCommand> where TAggregate : TransferableAggregateBase<TAggregateContents>, new()
    where TAggregateContents : IAggregateContents, new()
    where TAggregateCommand : ICreateAggregateCommand<TAggregate>
{
    public CustomerCreateBaseController(IAggregateCommandExecutor executor) : base(executor) { }
}
