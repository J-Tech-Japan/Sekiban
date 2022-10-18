using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients.Commands;
using Sekiban.Testing.Command;
using System;
namespace CustomerDomainXTest.AggregateTests.CommandsHelpers;

public class BranchClientCommandsHelper
{
    public static readonly string BranchName = "BranchName";
    public static readonly Guid BranchId = Guid.NewGuid();

    public static readonly Guid FirstClientId = Guid.NewGuid();
    public static readonly string FirstClientName = "Client Name";
    public static readonly string FirstClientEmail = "client@example.com";

    public static void CreateBranches(AggregateTestCommandExecutor ex)
    {
        ex.ExecuteCreateCommand(new CreateBranch(BranchName), BranchId);
    }
    public static void CreateClient(AggregateTestCommandExecutor ex)
    {
        CreateBranches(ex);
        ex.ExecuteCreateCommand(new CreateClient(BranchId, FirstClientName, FirstClientEmail), Guid.NewGuid());
    }
}
