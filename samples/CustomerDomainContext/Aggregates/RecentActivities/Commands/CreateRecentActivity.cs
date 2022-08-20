namespace CustomerDomainContext.Aggregates.RecentActivities.Commands
{
    public record CreateRecentActivity : ICreateAggregateCommand<RecentActivity>;
    public class CreateRecentActivityHandler : CreateAggregateCommandHandlerBase<RecentActivity, CreateRecentActivity>
    {
        public override Guid GenerateAggregateId(CreateRecentActivity command) =>
            Guid.NewGuid();
        protected override async Task ExecCreateCommandAsync(RecentActivity aggregate, CreateRecentActivity command)
        {
            await Task.CompletedTask;
            aggregate.CreateRecentActivity("First Event Created");
        }
    }
}
