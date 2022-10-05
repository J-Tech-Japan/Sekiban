using CustomerDomainContext.Aggregates.Branches.Commands;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Shared;
using CustomerDomainContext.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.TestHelpers;
using System;
using Xunit;
namespace CustomerDomainXTest.AggregateTests;

public class ClientSpec : SingleAggregateTestBase<Client, ClientContents>
{

    private const string testClientName = "TestName";
    private const string testClientChangedName = "TestName2";
    private const string testEmail = "test@example.com";
    private const string testClientChangedNameV3 = "TestName3";
    private static readonly Guid clientId = Guid.NewGuid();
    public ClientSpec() : base(CustomerDependency.GetOptions()) { }
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
        ThenSingleEventPayload(new ClientCreated(branchId, testClientName, testEmail));
        // 現在の集約のステータスを検証する
        ThenState(
            client => new AggregateDto<ClientContents>
            {
                AggregateId = client.AggregateId, Version = client.Version, Contents = new ClientContents(branchId, testClientName, testEmail)
            });
        // 名前変更コマンドを実行する
        WhenChange(client => new ChangeClientName(client.AggregateId, testClientChangedName) { ReferenceVersion = client.Version });
        WriteDtoToFile("ClientCreateSpec.json");
        // コマンドによって生成されたイベントを検証する
        ThenSingleEventPayload(new ClientNameChanged(testClientChangedName));
        // 現在の集約のステータスを検証する
        ThenState(
            client => new AggregateDto<ClientContents>
            {
                AggregateId = client.AggregateId,
                Version = client.Version,
                Contents = new ClientContents(branchId, testClientChangedName, testEmail)
            });
    }
    [Fact(DisplayName = "重複したメールアドレスが存在する場合、作成失敗する")]
    public void ClientCreateDuplicateEmailSpec()
    {
        var branchId = RunEnvironmentCreateCommand(new CreateBranch("TEST"));
        RunEnvironmentCreateCommand(new CreateClient(branchId, "NOT DUPLICATED NAME", testEmail));
        WhenCreate(new CreateClient(branchId, testClientName, testEmail)).ThenThrows<SekibanEmailAlreadyRegistered>();
    }
}
