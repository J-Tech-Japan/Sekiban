using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Aggregates.LoyaltyPoints.Commands;
using Customer.Domain.Aggregates.LoyaltyPoints.Consts;
using Customer.Domain.Shared;
using Customer.Domain.Shared.Exceptions;
using Customer.Test.AggregateTests.CommandsHelpers;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Testing.SingleProjections;
using System;
using Xunit;
using Client = Customer.Domain.Aggregates.Clients.Client;
namespace Customer.Test.AggregateTests;

public class ClientSpec : AggregateTestBase<Client, CustomerDependency>
{

    private const string testClientName = "TestName";
    private const string testClientChangedName = "TestName2";
    private const string testEmail = "test@example.com";
    private const string testClientChangedNameV3 = "TestName3";
    private static readonly Guid clientId = Guid.NewGuid();
    protected override void SetupDependency(IServiceCollection serviceCollection)
    {
        base.SetupDependency(serviceCollection);
    }
    [Fact(DisplayName = "集約コマンドを実行してテストする")]
    public void ClientCreateSpec()
    {
        // CreateコマンドでBranchを参照するため、Branch作成コマンドを流す
        var branchId = RunEnvironmentCreateCommand(new CreateBranch("TEST"));

        // Client コマンドを実行する
        WhenCreate(new Domain.Aggregates.Clients.Commands.Client(branchId, testClientName, testEmail));
        // エラーとならない
        ThenNotThrowsAnException();
        // コマンドによって生成されたイベントを検証する
        ThenLastSingleEventPayloadIs(new ClientCreated(branchId, testClientName, testEmail));
        // 現在の集約のステータスを検証する
        ThenStateIs(
            new AggregateState<Client>
            {
                AggregateId = GetAggregateId(), Version = GetCurrentVersion(), Payload = new Client(branchId, testClientName, testEmail)
            });
        // 名前変更コマンドを実行する
        WhenChange(client => new ChangeClientName(client.AggregateId, testClientChangedName) { ReferenceVersion = client.Version });
        WriteStateToFile("ClientCreateSpec.json");
        // コマンドによって生成されたイベントを検証する
        ThenLastSingleEventPayloadIs(new ClientNameChanged(testClientChangedName));
        // 現在の集約のステータスを検証する
        ThenStateIs(
            new AggregateState<Client>
            {
                AggregateId = GetAggregateId(),
                Version = GetCurrentVersion(),
                Payload = new Client(branchId, testClientChangedName, testEmail)
            });
    }
    [Fact(DisplayName = "重複したメールアドレスが存在する場合、作成失敗する")]
    public void ClientCreateDuplicateEmailSpec()
    {
        var branchId = RunEnvironmentCreateCommand(new CreateBranch("TEST"));
        RunEnvironmentCreateCommand(new Domain.Aggregates.Clients.Commands.Client(branchId, "NOT DUPLICATED NAME", testEmail));
        WhenCreate(new Domain.Aggregates.Clients.Commands.Client(branchId, testClientName, testEmail)).ThenThrows<SekibanEmailAlreadyRegistered>();
    }
    [Fact]
    public void UseCommandExecutor()
    {
        GivenEnvironmentCommandExecutorAction(BranchClientCommandsHelper.CreateClient)
            .WhenCreate(
                new Domain.Aggregates.Clients.Commands.Client(
                    BranchClientCommandsHelper.BranchId,
                    "Client Name New",
                    BranchClientCommandsHelper.FirstClientEmail))
            .ThenThrows<SekibanEmailAlreadyRegistered>();
    }
    [Fact]
    public void UseCommandExecutorSimpleThrows()
    {
        GivenEnvironmentCommandExecutorAction(BranchClientCommandsHelper.CreateClient)
            .WhenCreate(
                new Domain.Aggregates.Clients.Commands.Client(
                    BranchClientCommandsHelper.BranchId,
                    "Client Name New",
                    BranchClientCommandsHelper.FirstClientEmail))
            .ThenThrowsAnException();
    }
    [Fact]
    public void EnvironmentChangeCommandTest()
    {
        var branchId = RunEnvironmentCreateCommand(new CreateBranch("TEST"));
        var otherClientId = Guid.NewGuid();
        RunEnvironmentCreateCommand(
            new Domain.Aggregates.Clients.Commands.Client
                { BranchId = branchId, ClientName = "NameFirst", ClientEmail = "test@example.com" },
            otherClientId);
        RunEnvironmentChangeCommand(new ChangeClientName(otherClientId, "Other Client Name"));
        RunEnvironmentCreateCommand(new LoyaltyPoint(otherClientId, 100));
        RunEnvironmentChangeCommand(new UseLoyaltyPoint(otherClientId, DateTime.Today, LoyaltyPointUsageTypeKeys.TravelCarRental, 30, "test"));
    }

    [Fact(DisplayName = "Can not delete client twice")]
    public void CanNotDeleteClientTwice()
    {
        var branchId = RunEnvironmentCreateCommand(new CreateBranch("TEST"));
        WhenCreate(new Domain.Aggregates.Clients.Commands.Client(branchId, "client", "client@example.com"))
            .ThenNotThrowsAnException()
            .WhenChange(new DeleteClient(GetAggregateId()) { ReferenceVersion = GetCurrentVersion() })
            .ThenNotThrowsAnException()
            .WhenChange(new DeleteClient(GetAggregateId()) { ReferenceVersion = GetCurrentVersion() })
            .ThenThrows<SekibanAggregateAlreadyDeletedException>();
    }
    [Fact(DisplayName = "Can Cancel Delete")]
    public void CanCancelClientDelete()
    {
        var branchId = RunEnvironmentCreateCommand(new CreateBranch("TEST"));
        WhenCreate(new Domain.Aggregates.Clients.Commands.Client(branchId, "client", "client@example.com"))
            .ThenNotThrowsAnException()
            .WhenChange(new DeleteClient(GetAggregateId()) { ReferenceVersion = GetCurrentVersion() })
            .ThenNotThrowsAnException()
            .ThenGetPayload(payload => Assert.True(payload.IsDeleted))
            .WhenChange(
                new CancelDeleteClient
                    { ReferenceVersion = GetCurrentVersion(), ClientId = GetAggregateId(), Reason = "Deleted by mistake" })
            .ThenNotThrowsAnException()
            .ThenGetPayload(payload => Assert.False(payload.IsDeleted));
    }
}
