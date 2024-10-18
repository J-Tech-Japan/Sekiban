using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using ResultBoxes;
using Sekiban.Core.Usecase;
namespace FeatureCheck.Domain.Usecases;

public record AddBranchAndClientUsecase(string BranchName, string ClientName, string ClientEmail)
    : ISekibanUsecaseAsync<AddBranchAndClientUsecase, bool>
{

    public static Task<ResultBox<bool>> ExecuteAsync(AddBranchAndClientUsecase input, ISekibanUsecaseContext context) =>
        context
            .ExecuteCommand(new CreateBranchWithResult(input.BranchName))
            .Conveyor(response => response.GetAggregateId())
            .Conveyor(
                branchId => context.ExecuteCommand(new CreateClientR(branchId, input.ClientName, input.ClientEmail)))
            .Conveyor(() => true.ToResultBox());
}
