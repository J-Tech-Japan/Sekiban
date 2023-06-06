using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Aggregates.Clients;
using FeatureCheck.Domain.Aggregates.Clients.Commands;
using FeatureCheck.Domain.Aggregates.Clients.Events;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Commands;
using FeatureCheck.Domain.Aggregates.LoyaltyPoints.Consts;
using FeatureCheck.Domain.Shared;
using FeatureCheck.Domain.Shared.Exceptions;
using FeatureCheck.Test.AggregateTests.CommandsHelpers;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Testing.SingleProjections;
using System;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class ClientSpec : AggregateTest<Client, FeatureCheckDependency>
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
        var branchId = RunEnvironmentCommand(new CreateBranch("TEST"));
//Given 前提条件
        // CreateClient コマンドを実行する
        WhenCommand(new CreateClient(branchId, testClientName, testEmail));
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
        WhenCommand(client => new ChangeClientName(client.AggregateId, testClientChangedName) { ReferenceVersion = client.Version });
        WriteStateToFile("ClientCreateSpec.json");
        // コマンドによって生成されたイベントを検証する
        ThenLastSingleEventPayloadIs(new ClientNameChanged(testClientChangedName));
        // 現在の集約のステータスを検証する
        ThenStateIs(
            new AggregateState<Client>
            {
                AggregateId = GetAggregateId(), Version = GetCurrentVersion(), Payload = new Client(branchId, testClientChangedName, testEmail)
            });
    }

    [Fact(DisplayName = "重複したメールアドレスが存在する場合、作成失敗する")]
    public void ClientCreateDuplicateEmailSpec()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("TEST"));
        RunEnvironmentCommand(new CreateClient(branchId, "NOT DUPLICATED NAME", testEmail));
        WhenCommand(new CreateClient(branchId, testClientName, testEmail)).ThenThrows<SekibanEmailAlreadyRegistered>();
    }

    [Fact]
    public void UseCommandExecutor()
    {
        GivenEnvironmentCommandExecutorAction(BranchClientCommandsHelper.CreateClient)
            .WhenCommand(new CreateClient(BranchClientCommandsHelper.BranchId, "CreateClient Name New", BranchClientCommandsHelper.FirstClientEmail))
            .ThenThrows<SekibanEmailAlreadyRegistered>();
    }

    [Fact]
    public void UseCommandExecutorSimpleThrows()
    {
        GivenEnvironmentCommandExecutorAction(BranchClientCommandsHelper.CreateClient)
            .WhenCommand(new CreateClient(BranchClientCommandsHelper.BranchId, "CreateClient Name New", BranchClientCommandsHelper.FirstClientEmail))
            .ThenThrowsAnException();
    }

    [Fact]
    public void EnvironmentChangeCommandTest()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("TEST"));
        var otherClientId = Guid.NewGuid();
        RunEnvironmentCommand(new CreateClient { BranchId = branchId, ClientName = "NameFirst", ClientEmail = "test@example.com" }, otherClientId);
        RunEnvironmentCommand(new ChangeClientName(otherClientId, "Other CreateClient Name"));
        RunEnvironmentCommand(new CreateLoyaltyPoint(otherClientId, 100));
        RunEnvironmentCommand(new UseLoyaltyPoint(otherClientId, DateTime.Today, LoyaltyPointUsageTypeKeys.TravelCarRental, 30, "test"));
    }

    [Fact(DisplayName = "Can not delete client twice")]
    public void CanNotDeleteClientTwice()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("TEST"));
        WhenCommand(new CreateClient(branchId, "client", "client@example.com"))
            .ThenNotThrowsAnException()
            .WhenCommand(new DeleteClient(GetAggregateId()) { ReferenceVersion = GetCurrentVersion() })
            .ThenNotThrowsAnException()
            .WhenCommand(new DeleteClient(GetAggregateId()) { ReferenceVersion = GetCurrentVersion() })
            .ThenThrows<SekibanAggregateAlreadyDeletedException>();
    }

    [Fact(DisplayName = "Can Cancel Delete")]
    public void CanCancelClientDelete()
    {
        var branchId = RunEnvironmentCommand(new CreateBranch("TEST"));
        WhenCommand(new CreateClient(branchId, "client", "client@example.com"))
            .ThenNotThrowsAnException()
            .WhenCommand(new DeleteClient(GetAggregateId()) { ReferenceVersion = GetCurrentVersion() })
            .ThenNotThrowsAnException()
            .ThenGetPayload(payload => Assert.True(payload.IsDeleted))
            .WhenCommand(
                new CancelDeleteClient { ReferenceVersion = GetCurrentVersion(), ClientId = GetAggregateId(), Reason = "Deleted by mistake" })
            .ThenNotThrowsAnException()
            .ThenGetPayload(payload => Assert.False(payload.IsDeleted));
    }
}
