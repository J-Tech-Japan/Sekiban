using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Clients.Commands;
using Sekiban.Testing.Command;
using System;
namespace Customer.Test.AggregateTests.CommandsHelpers;

public class BranchClientCommandsHelper
{
    public static readonly string BranchName = "BranchName";
    public static readonly Guid BranchId = Guid.NewGuid();

    public static readonly Guid FirstClientId = Guid.NewGuid();
    public static readonly string FirstClientName = "CreateClient Name";
    public static readonly string FirstClientEmail = "client@example.com";

    public static void CreateBranches(TestCommandExecutor ex)
    {
        ex.ExecuteCommand(new CreateBranch(BranchName), BranchId);
    }
    public static void CreateClient(TestCommandExecutor ex)
    {
        CreateBranches(ex);
        ex.ExecuteCommand(new CreateClient(BranchId, FirstClientName, FirstClientEmail), Guid.NewGuid());
    }
}
