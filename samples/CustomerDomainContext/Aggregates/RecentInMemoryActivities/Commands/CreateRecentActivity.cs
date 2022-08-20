namespace CustomerDomainContext.Aggregates.RecentInMemoryActivities.Commands
{
    public record CreateRecentInMemoryActivity : ICreateAggregateCommand<RecentInMemoryActivity>;
    public class CreateRecentInMemoryActivityHandler : CreateAggregateCommandHandlerBase<RecentInMemoryActivity, CreateRecentInMemoryActivity>
    {
        public override Guid GenerateAggregateId(CreateRecentInMemoryActivity command) =>
            Guid.NewGuid();
        protected override async Task ExecCreateCommandAsync(RecentInMemoryActivity aggregate, CreateRecentInMemoryActivity command)
        {
            await Task.CompletedTask;
            aggregate.CreateRecentInMemoryActivity("First Event Created");
        }
    }
}
