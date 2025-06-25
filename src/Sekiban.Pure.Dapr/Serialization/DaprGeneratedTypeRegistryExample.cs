using System.CodeDom.Compiler;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Example of what the Source Generator would produce for domain type registration
/// This file serves as a template for the actual generated code
/// </summary>
[GeneratedCode("Sekiban.Pure.SourceGenerator", "1.0.0")]
public static class DaprGeneratedTypeRegistryExample
{
    /// <summary>
    /// Registers all domain types with the type registry
    /// This method would be automatically generated based on types found in the domain
    /// </summary>
    /// <param name="registry">The type registry to populate</param>
    public static void RegisterAll(IDaprTypeRegistry registry)
    {
        // Commands
        registry.RegisterType<CreateUserCommand>("CreateUser");
        registry.RegisterType<UpdateUserCommand>("UpdateUser");
        registry.RegisterType<DeleteUserCommand>("DeleteUser");
        
        // Events
        registry.RegisterType<UserCreatedEvent>("UserCreated");
        registry.RegisterType<UserUpdatedEvent>("UserUpdated");
        registry.RegisterType<UserDeletedEvent>("UserDeleted");
        
        // Aggregate Payloads
        registry.RegisterType<UserAggregate>("User");
        registry.RegisterType<EmptyUserAggregate>("EmptyUser");
        
        // Multi-Projection Payloads
        registry.RegisterType<UserSummaryProjection>("UserSummary");
        registry.RegisterType<UserStatisticsProjection>("UserStatistics");
    }
    
    // Example command types (these would be in the actual domain)
    private record CreateUserCommand(string Name, string Email);
    private record UpdateUserCommand(Guid UserId, string Name);
    private record DeleteUserCommand(Guid UserId);
    
    // Example event types
    private record UserCreatedEvent(Guid UserId, string Name, string Email);
    private record UserUpdatedEvent(Guid UserId, string Name);
    private record UserDeletedEvent(Guid UserId);
    
    // Example aggregate types
    private record UserAggregate(Guid UserId, string Name, string Email, bool IsDeleted);
    private record EmptyUserAggregate();
    
    // Example projection types
    private record UserSummaryProjection(int TotalUsers, int ActiveUsers);
    private record UserStatisticsProjection(Dictionary<string, int> UsersByDomain);
}