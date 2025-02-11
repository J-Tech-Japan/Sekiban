using Pure.Domain.Generated;
using ResultBoxes;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using Sekiban.Pure.Repositories;
namespace Pure.Domain.Test;

public class MultiProjectionSpec
{
    [Fact]
    public async Task TestSimple()
    {
        Repository.Events.Clear();
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
        var result = await executor
            .Execute(
                new RegisterUser("Tomohisa", "tomo@example.com"),
                new RegisterUser.Injection(email => false),
                CommandMetadata.Create("test"))
            .Conveyor(
                response => executor.Execute(
                    new ConfirmUser(response.PartitionKeys.AggregateId),
                    CommandMetadata.Create("test")))
            .Conveyor(
                _ => executor.Execute(
                    new RegisterUser("John", "john@example.com"),
                    new RegisterUser.Injection(_ => false),
                    CommandMetadata.Create("test")))
            .Conveyor(
                response => executor.Execute(
                    new ConfirmUser(response.PartitionKeys.AggregateId),
                    CommandMetadata.Create("test")))
            .Conveyor(
                response => executor.Execute(
                    new RevokeUser(response.PartitionKeys.AggregateId),
                    new RevokeUser.Injection(_ => true),
                    CommandMetadata.Create("test")))
            .Conveyor(_ => executor.Execute(new RegisterBranch("japan"), CommandMetadata.Create("test")));
        Assert.True(result.IsSuccess);
        var projectionResult
            = await Repository.LoadMultiProjection<MultiProjectorPayload>(MultiProjectionEventSelector.All);
        Assert.True(projectionResult.IsSuccess);
        var projection = projectionResult.GetValue();
        Assert.Equal(2, projection.Payload.Users.Count);
        Assert.Equal(1, projection.Payload.Users.Values.Count(m => m.IsConfirmed));

        var projectionFromAggregateList
            = await Repository.LoadMultiProjection<AggregateListProjector<UserProjector>>(
                MultiProjectionEventSelector.FromProjectorGroup<UserProjector>());
        // var projectionFromAggregateList
        //     = Repository.LoadMultiProjection<AggregateListProjector<UserProjector>>(MultiProjectionEventSelector.All);
        Assert.True(projectionFromAggregateList.IsSuccess);
        var projectionFromAggregateListValue = projectionFromAggregateList.GetValue();
        Assert.Equal(2, projectionFromAggregateListValue.Payload.Aggregates.Count);
        Assert.Equal(
            1,
            projectionFromAggregateListValue.Payload.Aggregates.Count(m => m.Value.GetPayload() is ConfirmedUser));
        var queryExecutor = new QueryExecutor();
        var queryResult = await queryExecutor.Execute(
            new UserQueryFromMultiProjection(),
            Repository.LoadMultiProjection<MultiProjectorPayload>);
        // var queryResult
        //     = await queryExecutor
        //         .ExecuteListWithMultiProjectionFunction<MultiProjectorPayload, UserQueryFromMultiProjection, string>(
        //             new UserQueryFromMultiProjection(),
        //             UserQueryFromMultiProjection.HandleFilter,
        //             UserQueryFromMultiProjection.HandleSort);


        Assert.True(queryResult.IsSuccess);
        var queryResultValue = queryResult.GetValue().Items.ToList();
        Assert.Equal(2, queryResultValue.Count);

    }

    [Fact]
    public async Task TestUserQueryFromAggregateProjection()
    {
        // Setup test data
        Repository.Events.Clear();
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
        var result = await executor
            // Register and confirm first user "Alice"
            .Execute(
                new RegisterUser("Alice", "alice@example.com"),
                new RegisterUser.Injection(email => false),
                CommandMetadata.Create("test"))
            .Conveyor(
                response => executor.Execute(
                    new ConfirmUser(response.PartitionKeys.AggregateId),
                    CommandMetadata.Create("test")))
            // Register and confirm second user "Bob"
            .Conveyor(
                _ => executor.Execute(
                    new RegisterUser("Bob", "bob@example.com"),
                    new RegisterUser.Injection(_ => false),
                    CommandMetadata.Create("test")))
            .Conveyor(
                response => executor.Execute(
                    new ConfirmUser(response.PartitionKeys.AggregateId),
                    CommandMetadata.Create("test")))
            // Register third user "Charlie" but don't confirm
            .Conveyor(
                _ => executor.Execute(
                    new RegisterUser("Charlie", "charlie@example.com"),
                    new RegisterUser.Injection(_ => false),
                    CommandMetadata.Create("test")));

        Assert.True(result.IsSuccess);

        var queryExecutor = new QueryExecutor();

        // Test 1: Query with empty filter (should return all confirmed users)
        var allUsersQuery = await queryExecutor.Execute(
            new UserQueryFromAggregateProjection(""),
            Repository.LoadMultiProjection<AggregateListProjector<UserProjector>>);
        Assert.True(allUsersQuery.IsSuccess);
        var allUsers = allUsersQuery.GetValue().Items.ToList();
        Assert.Equal(2, allUsers.Count); // Only confirmed users (Alice and Bob)
        Assert.Equal("Alice", allUsers[0]); // Verify sorting
        Assert.Equal("Bob", allUsers[1]);

        // Test 2: Query with name filter
        var filteredQuery = await queryExecutor.Execute(
            new UserQueryFromAggregateProjection("ob"),
            Repository.LoadMultiProjection<AggregateListProjector<UserProjector>>); // Should match "Bob"
        Assert.True(filteredQuery.IsSuccess);
        var filteredUsers = filteredQuery.GetValue().Items.ToList();
        Assert.Single(filteredUsers);
        Assert.Equal("Bob", filteredUsers[0]);

        // Test 3: Query with non-matching filter
        var noMatchQuery = await queryExecutor.Execute(
            new UserQueryFromAggregateProjection("xyz"),
            Repository.LoadMultiProjection<AggregateListProjector<UserProjector>>);
        Assert.True(noMatchQuery.IsSuccess);
        var noMatchUsers = noMatchQuery.GetValue().Items.ToList();
        Assert.Empty(noMatchUsers);
    }

    [Fact]
    public async Task TestUserExistsQuery()
    {
        // Setup test data
        Repository.Events.Clear();
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
        var result = await executor
            // Register first user
            .Execute(
                new RegisterUser("Alice", "alice@example.com"),
                new RegisterUser.Injection(email => false),
                CommandMetadata.Create("test"))
            // Register second user
            .Conveyor(
                _ => executor.Execute(
                    new RegisterUser("Bob", "bob@example.com"),
                    new RegisterUser.Injection(_ => false),
                    CommandMetadata.Create("test")));

        Assert.True(result.IsSuccess);

        var queryExecutor = new QueryExecutor();

        // Test 1: Query with existing email
        var existingEmailQuery = await queryExecutor.Execute(
            new UserExistsQueryFromMultiProjection("alice@example.com"),
            Repository.LoadMultiProjection<MultiProjectorPayload>);
        Assert.True(existingEmailQuery.IsSuccess);
        Assert.True(existingEmailQuery.GetValue());

        // Test 2: Query with another existing email
        var anotherExistingEmailQuery = await queryExecutor.Execute(
            new UserExistsQueryFromMultiProjection("bob@example.com"),
            Repository.LoadMultiProjection<MultiProjectorPayload>);
        Assert.True(anotherExistingEmailQuery.IsSuccess);
        Assert.True(anotherExistingEmailQuery.GetValue());

        // Test 3: Query with non-existing email
        var nonExistingEmailQuery = await queryExecutor.Execute(
            new UserExistsQueryFromMultiProjection("nonexistent@example.com"),
            Repository.LoadMultiProjection<MultiProjectorPayload>);
        Assert.True(nonExistingEmailQuery.IsSuccess);
        Assert.False(nonExistingEmailQuery.GetValue());
    }

    [Fact]
    public async Task TestUserExistsQueryFromAggregateListProjection()
    {
        // Setup test data
        Repository.Events.Clear();
        var executor = new CommandExecutor { EventTypes = new PureDomainEventTypes() };
        var result = await executor
            // Register first user
            .Execute(
                new RegisterUser("Alice", "alice@example.com"),
                new RegisterUser.Injection(email => false),
                CommandMetadata.Create("test"))
            // Register second user
            .Conveyor(
                _ => executor.Execute(
                    new RegisterUser("Bob", "bob@example.com"),
                    new RegisterUser.Injection(_ => false),
                    CommandMetadata.Create("test")));

        Assert.True(result.IsSuccess);

        var queryExecutor = new QueryExecutor();

        // Test 1: Query with existing email
        var existingEmailQuery = await queryExecutor.Execute(
            new UserExistsQueryFromAggregateListProjection("alice@example.com"),
            Repository.LoadMultiProjection<AggregateListProjector<UserProjector>>);
        Assert.True(existingEmailQuery.IsSuccess);
        Assert.True(existingEmailQuery.GetValue());

        // Test 2: Query with another existing email
        var anotherExistingEmailQuery = await queryExecutor.Execute(
            new UserExistsQueryFromAggregateListProjection("bob@example.com"),
            Repository.LoadMultiProjection<AggregateListProjector<UserProjector>>);
        Assert.True(anotherExistingEmailQuery.IsSuccess);
        Assert.True(anotherExistingEmailQuery.GetValue());

        // Test 3: Query with non-existing email
        var nonExistingEmailQuery = await queryExecutor.Execute(
            new UserExistsQueryFromAggregateListProjection("nonexistent@example.com"),
            Repository.LoadMultiProjection<AggregateListProjector<UserProjector>>);
        Assert.True(nonExistingEmailQuery.IsSuccess);
        Assert.False(nonExistingEmailQuery.GetValue());
    }
}
