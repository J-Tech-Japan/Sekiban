using Customer.Domain.Aggregates.Branches.Commands;
using Customer.Domain.Aggregates.Clients;
using Customer.Domain.Aggregates.Clients.Commands;
using Customer.Domain.Aggregates.Clients.Events;
using Customer.Domain.Shared;
using Customer.Domain.Shared.Exceptions;
using Customer.Test.AggregateTests.CommandsHelpers;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Testing.SingleAggregate;
using System;
using Xunit;
namespace Customer.Test.AggregateTests;

public class ClientSpec : SingleAggregateTestBase<Client, CustomerDependency>
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
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照用に渡す
        var branchId = RunEnvironmentCreateCommand(new CreateBranch("TEST"));

        // CreateClient コマンドを実行する
        WhenCreate(new CreateClient(branchId, testClientName, testEmail));
        // エラーとならない
        ThenNotThrowsAnException();
        // コマンドによって生成されたイベントを検証する
        ThenSingleEventPayloadIs(new ClientCreated(branchId, testClientName, testEmail));
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
        ThenSingleEventPayloadIs(new ClientNameChanged(testClientChangedName));
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
        RunEnvironmentCreateCommand(new CreateClient(branchId, "NOT DUPLICATED NAME", testEmail));
        WhenCreate(new CreateClient(branchId, testClientName, testEmail)).ThenThrows<SekibanEmailAlreadyRegistered>();
    }
    [Fact]
    public void UseCommandExecutor()
    {
        GivenEnvironmentCommandExecutorAction(BranchClientCommandsHelper.CreateClient)
            .WhenCreate(new CreateClient(BranchClientCommandsHelper.BranchId, "ClientAgggg Name New", BranchClientCommandsHelper.FirstClientEmail))
            .ThenThrows<SekibanEmailAlreadyRegistered>();
    }
}
