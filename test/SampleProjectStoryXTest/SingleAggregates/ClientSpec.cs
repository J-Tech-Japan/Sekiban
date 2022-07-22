using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Shared.Exceptions;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using System;
using System.Collections.Generic;
using Xunit;
namespace SampleProjectStoryXTest.SingleAggregates;

public class ClientSpec : SampleSingleAggregateTestBase<Client, ClientContents>
{
    private const string testClientName = "TestName";
    private const string testClientChangedName = "TestName2";
    private const string testEmail = "test@example.com";
    private const string testClientChangedNameV3 = "TestName3";

    [Fact(DisplayName = "集約コマンドを実行してテストする")]
    public void ClientCreateSpec()
    {
        var branchDto = new AggregateDto<BranchContents>
        {
            AggregateId = Guid.NewGuid(), Contents = new BranchContents { Name = "TEST" }, Version = 1
        };
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照ように渡す
        GivenEnvironmentDtos(new List<ISingleAggregate> { branchDto });
        // CreateClient コマンドを実行する
        WhenCreate(new CreateClient(branchDto.AggregateId, testClientName, testEmail));
        // エラーとならない
        ThenNotThrowsAnException();
        // コマンドによって生成されたイベントを検証する
        ThenSingleEvent(client => new ClientCreated(client.AggregateId, branchDto.AggregateId, testClientName, testEmail));
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
        // コマンドによって生成されたイベントを検証する
        ThenSingleEvent(client => new ClientNameChanged(client.AggregateId, testClientChangedName));
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
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照ように渡す
        GivenEnvironmentDtos(new List<ISingleAggregate> { branchDto, clientDto });
        // CreateClient コマンドを実行する エラーになるはず
        WhenCreate(new CreateClient(branchDto.AggregateId, testClientName, testEmail)).ThenThrows<SekibanEmailAlreadyRegistered>();
    }
    [Fact(DisplayName = "コマンドではなく、集約メソッドをテストする")]
    public void UsingAggregateFunctionNoCommand()
    {
        var branchId = Guid.NewGuid();

        WhenConstructor(() => new Client(branchId, testClientName, testEmail))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent(client => new ClientCreated(client.AggregateId, branchId, testClientName, testEmail))
            // 現在の集約のステータスを検証する
            .ThenState(
                client => new AggregateDto<ClientContents>
                {
                    AggregateId = client.AggregateId, Version = client.Version, Contents = new ClientContents(branchId, testClientName, testEmail)
                })
            .WhenMethod(
                aggregate =>
                {
                    aggregate.ChangeClientName(testClientChangedName);
                })
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent(client => new ClientNameChanged(client.AggregateId, testClientChangedName))
            // 現在の集約のステータスを検証する
            .ThenState(
                client => new AggregateDto<ClientContents>
                {
                    AggregateId = client.AggregateId,
                    Version = client.Version,
                    Contents = new ClientContents(branchId, testClientChangedName, testEmail)
                });
    }
    [Fact(DisplayName = "イベントを渡してスタートする")]
    public void StartWithEvents()
    {
        var branchId = Guid.NewGuid();
        Given(new ClientCreated(Guid.NewGuid(), branchId, testClientName, testEmail))
            .Given(client => new ClientNameChanged(client.AggregateId, testClientChangedName))
            .WhenMethod(client => client.ChangeClientName(testClientChangedNameV3))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent(client => new ClientNameChanged(client.AggregateId, testClientChangedNameV3))
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

        Given(
                new AggregateDto<ClientContents>
                {
                    AggregateId = clientId, Version = 1, Contents = new ClientContents(branchId, testClientName, testEmail)
                })
            .WhenMethod(client => client.ChangeClientName(testClientChangedName))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent(client => new ClientNameChanged(client.AggregateId, testClientChangedName))
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
