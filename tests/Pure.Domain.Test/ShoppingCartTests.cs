using Microsoft.Extensions.DependencyInjection;
using Pure.Domain.Generated;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Repositories;
namespace Pure.Domain.Test;

public class ShoppingCartTests
{
    [Fact]
    public async Task ShoppingCartSpec()
    {
        var executor = new InMemorySekibanExecutor(
            PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
            new FunctionCommandMetadataProvider(() => "test"),
            new Repository(),new ServiceCollection().BuildServiceProvider());
        var userId = Guid.NewGuid();
        var createCommand = new CreateShoppingCart(userId);
        var result = await executor.ExecuteCommandAsync(createCommand);
        Assert.True(result.IsSuccess);
        var aggregate = executor.Repository.Load<ShoppingCartProjector>(result.GetValue().PartitionKeys);
        Assert.NotNull(aggregate);
        Assert.IsType<BuyingShoppingCart>(aggregate.GetValue().GetPayload());
        var buyingShoppingCart
            = aggregate.GetValue().GetPayload() as BuyingShoppingCart ?? throw new ApplicationException();
        Assert.Equal(userId, buyingShoppingCart.UserId);
    }

    // [Fact]
    // public async Task ShoppingCartSpecFunction()
    // {
    //     var executor = new InMemorySekibanExecutor(
    //         PureDomainDomainTypes.Generate(PureDomainEventsJsonContext.Default.Options),
    //         new FunctionCommandMetadataProvider(() => "test"),
    //         new Repository());
    //     var userId = Guid.NewGuid();
    //     var createCommand = new CreateShoppingCart(userId);
    //     var result = await executor.ExecuteFunctionAsync(
    //         createCommand,
    //         new ShoppingCartProjector(),
    //         createCommand.SpecifyPartitionKeys,
    //         createCommand.HandleAsync,
    //         CommandMetadata.Create("test"));
    //     Assert.True(result.IsSuccess);
    //     var aggregate = Repository.Load<ShoppingCartProjector>(result.GetValue().PartitionKeys);
    //     Assert.NotNull(aggregate);
    //     Assert.IsType<BuyingShoppingCart>(aggregate.GetValue().GetPayload());
    //     var buyingShoppingCart
    //         = aggregate.GetValue().GetPayload() as BuyingShoppingCart ?? throw new ApplicationException();
    //     Assert.Equal(userId, buyingShoppingCart.UserId);
    // }
}
