using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Shared;
using CustomerDomainContext.Shared.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.AggregateEvents;
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
        var branchDto = new AggregateDto<BranchContents>
        {
            AggregateId = Guid.NewGuid(), Contents = new BranchContents { Name = "TEST" }, Version = 1
        };
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照用に渡す
        //GivenEnvironmentDtos(new List<ISingleAggregate> { branchDto });
        // CreateClient コマンドを実行する
        WhenCreate(new CreateClient(branchDto.AggregateId, testClientName, testEmail));
        // エラーとならない
        ThenNotThrowsAnException();
        // コマンドによって生成されたイベントを検証する
        ThenSingleEventPayload(new ClientCreated(branchDto.AggregateId, testClientName, testEmail));
        // 現在の集約のステータスを検証する
        ThenState(
            client => new AggregateDto<ClientContents>
            {
                AggregateId = client.AggregateId,
                Version = client.Version,
                Contents = new ClientContents(branchDto.AggregateId, testClientName, testEmail)
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
                Contents = new ClientContents(branchDto.AggregateId, testClientChangedName, testEmail)
            });
    }
    [Fact(DisplayName = "重複したメールアドレスが存在する場合、作成失敗する")]
    public void ClientCreateDuplicateEmailSpec()
    {
        var branchDto = new AggregateDto<BranchContents>
        {
            AggregateId = Guid.NewGuid(), Contents = new BranchContents { Name = "TEST" }, Version = 1
        };
        var clientDto = new AggregateDto<ClientContents>
        {
            AggregateId = Guid.NewGuid(), Version = 1, Contents = new ClientContents(Guid.NewGuid(), "NOT DUPLICATED NAME", testEmail)
        };
        // GivenEnvironmentDtoContents<Branch, BranchContents>(Guid.NewGuid(), new BranchContents { Name = "TEST" });
        // GivenEnvironmentDtoContents<Client, ClientContents>(Guid.NewGuid(), new ClientContents(Guid.NewGuid(), "NOT DUPLICATED NAME", testEmail));
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照用に渡す
        // GivenEnvironmentDtos(new List<ISingleAggregate> { branchDto, clientDto });
        // CreateClient コマンドを実行する エラーになるはず
        WhenCreate(new CreateClient(branchDto.AggregateId, testClientName, testEmail)).ThenThrows<SekibanEmailAlreadyRegistered>();
    }

    [Fact(DisplayName = "イベントを渡してスタートする")]
    public void StartWithEvents()
    {
        var branchId = Guid.NewGuid();
        var clientId = Guid.NewGuid();
        Given(AggregateEvent<ClientCreated>.CreatedEvent(clientId, typeof(Client), new ClientCreated(branchId, testClientName, testEmail)))
            .Given(new ClientNameChanged(testClientChangedName))
            // when method は廃止
//            .WhenMethod(client => client.ChangeClientName(testClientChangedNameV3))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEventPayload(new ClientNameChanged(testClientChangedNameV3))
            // 現在の集約のステータスを検証する
            .ThenState(
                client => new AggregateDto<ClientContents>
                {
                    AggregateId = client.AggregateId,
                    Version = client.Version,
                    Contents = new ClientContents(branchId, testClientChangedNameV3, testEmail)
                });
    }
    [Fact(DisplayName = "スナップショットを使用してテストを開始")]
    public void StartWithSnapshot()
    {
        var branchId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        Given(clientId, new ClientContents(branchId, testClientName, testEmail))
            // when method は廃止
            // .WhenMethod(client => client.ChangeClientName(testClientChangedName))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEventPayload(new ClientNameChanged(testClientChangedName))
            // 現在の集約のステータスを検証する
            .ThenState(
                client => new AggregateDto<ClientContents>
                {
                    AggregateId = client.AggregateId,
                    Version = client.Version,
                    Contents = new ClientContents(branchId, testClientChangedName, testEmail)
                });
    }
}
