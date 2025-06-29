using System;
using System.Threading.Tasks;
using DaprSample.Domain.User;
using DaprSample.Domain.User.Commands;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Executors;
using Sekiban.Pure;
using Xunit;
using Xunit.Abstractions;

namespace DaprSample.Tests;

public class VersionIncrementTest : IAsyncLifetime
{
    private readonly ITestOutputHelper _output;
    private ServiceProvider? _serviceProvider;
    private ISekibanExecutor? _executor;

    public VersionIncrementTest(ITestOutputHelper output)
    {
        _output = output;
    }

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        
        // Configure logging to test output
        services.AddLogging(builder => 
        {
            builder.AddXunit(_output);
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // Add required services for testing
        services.AddMemoryCache();
        
        // Generate domain types
        var domainTypes = DaprSample.Domain.Generated.DaprSampleDomainDomainTypes.Generate(
            DaprSample.Domain.DaprSampleEventsJsonContext.Default.Options);
        services.AddSingleton(domainTypes);

        // Use in-memory executor for testing
        services.AddSekibanInMemory();
        
        _serviceProvider = services.BuildServiceProvider();
        _executor = _serviceProvider.GetRequiredService<ISekibanExecutor>();
        
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _serviceProvider?.Dispose();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task UpdateUserName_WithSameName_ShouldNotIncrementVersion()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var userName = "Test User";
        
        // Act 1: Create user
        var createCommand = new CreateUser(userId, userName, "test@example.com");
        var createResult = await _executor!.CommandAsync(createCommand);
        Assert.True(createResult.IsSuccess);
        
        var versionAfterCreate = createResult.GetValue().Version;
        _output.WriteLine($"Version after create: {versionAfterCreate}");
        Assert.Equal(1, versionAfterCreate);
        
        // Act 2: Update with same name (should produce no event)
        var updateCommand1 = new UpdateUserName(userId, userName);
        var updateResult1 = await _executor.CommandAsync(updateCommand1);
        Assert.True(updateResult1.IsSuccess);
        
        var versionAfterSameNameUpdate = updateResult1.GetValue().Version;
        _output.WriteLine($"Version after same name update: {versionAfterSameNameUpdate}");
        _output.WriteLine($"Events produced: {updateResult1.GetValue().Events.Count}");
        
        // Assert: Version should not change when no event is produced
        Assert.Equal(versionAfterCreate, versionAfterSameNameUpdate);
        Assert.Empty(updateResult1.GetValue().Events);
        
        // Act 3: Update with different name (should produce event)
        var newName = "New Name";
        var updateCommand2 = new UpdateUserName(userId, newName);
        var updateResult2 = await _executor.CommandAsync(updateCommand2);
        Assert.True(updateResult2.IsSuccess);
        
        var versionAfterNameChange = updateResult2.GetValue().Version;
        _output.WriteLine($"Version after name change: {versionAfterNameChange}");
        _output.WriteLine($"Events produced: {updateResult2.GetValue().Events.Count}");
        
        // Assert: Version should increment when event is produced
        Assert.Equal(versionAfterCreate + 1, versionAfterNameChange);
        Assert.Single(updateResult2.GetValue().Events);
        
        // Act 4: Load aggregate to verify final state
        var loadResult = await _executor.LoadAggregateAsync<UserProjector>(
            PartitionKeys.Existing<UserProjector>(userId));
        Assert.True(loadResult.IsSuccess);
        
        var aggregate = loadResult.GetValue();
        _output.WriteLine($"Final aggregate version: {aggregate.Version}");
        _output.WriteLine($"Final aggregate state: {aggregate.Payload}");
        
        Assert.Equal(versionAfterNameChange, aggregate.Version);
        Assert.Equal(2, aggregate.Version); // Should be 2: 1 for create, 1 for name change
    }

    [Fact]
    public async Task UpdateUserName_MultipleNoOpUpdates_ShouldNotIncrementVersion()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var userName = "Test User";
        
        // Create user
        var createCommand = new CreateUser(userId, userName, "test@example.com");
        var createResult = await _executor!.CommandAsync(createCommand);
        Assert.True(createResult.IsSuccess);
        Assert.Equal(1, createResult.GetValue().Version);
        
        // Perform multiple updates with same name
        for (int i = 0; i < 5; i++)
        {
            var updateCommand = new UpdateUserName(userId, userName);
            var updateResult = await _executor.CommandAsync(updateCommand);
            Assert.True(updateResult.IsSuccess);
            
            _output.WriteLine($"Update {i + 1}: Version = {updateResult.GetValue().Version}, Events = {updateResult.GetValue().Events.Count}");
            
            // Version should remain 1
            Assert.Equal(1, updateResult.GetValue().Version);
            Assert.Empty(updateResult.GetValue().Events);
        }
        
        // Verify final state
        var loadResult = await _executor.LoadAggregateAsync<UserProjector>(
            PartitionKeys.Existing<UserProjector>(userId));
        Assert.True(loadResult.IsSuccess);
        Assert.Equal(1, loadResult.GetValue().Version);
    }
}