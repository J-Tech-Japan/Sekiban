namespace CustomerDomainContext.Aggregates.LoyaltyPoints.Commands
{
    public record DeleteLoyaltyPoint(Guid ClientId) : ChangeAggregateCommandBase<LoyaltyPoint>, INoValidateCommand
    {
        public DeleteLoyaltyPoint() : this(Guid.Empty) { }
        public override Guid GetAggregateId() =>
            ClientId;
    }
    public class DeleteLoyaltyPointHandler : ChangeAggregateCommandHandlerBase<LoyaltyPoint, DeleteLoyaltyPoint>
    {
        protected override async Task ExecCommandAsync(LoyaltyPoint aggregate, DeleteLoyaltyPoint _)
        {
            aggregate.Delete();
            await Task.CompletedTask;
        }
    }
}
