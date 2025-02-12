using Pure.Domain.Generated;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Repositories;
namespace Pure.Domain.Test;

public class ShoppingCartTests
{
    [Fact]
    public async Task ShoppingCartSpec()
    {
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
        var userId = Guid.NewGuid();
        var createCommand = new CreateShoppingCart(userId);
        var result = await executor.Execute(createCommand, CommandMetadata.Create("test"));
        Assert.True(result.IsSuccess);
        var aggregate = Repository.Load<ShoppingCartProjector>(result.GetValue().PartitionKeys);
        Assert.NotNull(aggregate);
        Assert.IsType<BuyingShoppingCart>(aggregate.GetValue().GetPayload());
        var buyingShoppingCart
            = aggregate.GetValue().GetPayload() as BuyingShoppingCart ?? throw new ApplicationException();
        Assert.Equal(userId, buyingShoppingCart.UserId);
    }

    [Fact]
    public async Task ShoppingCartSpecFunction()
    {
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
        var userId = Guid.NewGuid();
        var createCommand = new CreateShoppingCart(userId);
        var result = await executor.ExecuteFunctionAsync(
            createCommand,
            new ShoppingCartProjector(),
            createCommand.SpecifyPartitionKeys,
            createCommand.HandleAsync,
            CommandMetadata.Create("test"));
        Assert.True(result.IsSuccess);
        var aggregate = Repository.Load<ShoppingCartProjector>(result.GetValue().PartitionKeys);
        Assert.NotNull(aggregate);
        Assert.IsType<BuyingShoppingCart>(aggregate.GetValue().GetPayload());
        var buyingShoppingCart
            = aggregate.GetValue().GetPayload() as BuyingShoppingCart ?? throw new ApplicationException();
        Assert.Equal(userId, buyingShoppingCart.UserId);
    }
}
