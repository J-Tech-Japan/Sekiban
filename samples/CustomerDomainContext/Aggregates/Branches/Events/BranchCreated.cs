namespace CustomerDomainContext.Aggregates.Branches.Events
{
    public record BranchCreated(string Name) : ICreatedEventPayload;
}
