using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Events;
using CustomerDomainContext.Shared.Exceptions;
using Sekiban.EventSourcing.Aggregates;
using System;
using System.Collections.Generic;
using Xunit;
namespace SampleProjectStoryXTest.SingleAggregates;

public class ClientSpec : SampleSingleAggregateTestBase<Client, ClientDto>
{
    private const string testClientName = "TestName";
    private const string testClientChangedName = "TestName2";
    private const string testEmail = "test@example.com";
    private const string testClientChangedNameV3 = "TestName3";

    [Fact(DisplayName = "集約コマンドを実行してテストする")]
    public void ClientCreateSpec()
    {
        var branchDto = new BranchDto { AggregateId = Guid.NewGuid(), Name = "TEST", Version = 1 };
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照ように渡す
        GivenEnvironmentDtos(new List<AggregateDtoBase> { branchDto });
        // CreateClient コマンドを実行する
        WhenCreate(new CreateClient(branchDto.AggregateId, testClientName, testEmail));
        // コマンドによって生成されたイベントを検証する
        ThenSingleEvent(client => new ClientCreated(client.AggregateId, branchDto.AggregateId, testClientName, testEmail));
        // 現在の集約のステータスを検証する
        ThenState(
            client => new ClientDto
            {
                AggregateId = client.AggregateId,
                BranchId = branchDto.AggregateId,
                ClientEmail = testEmail,
                ClientName = testClientName,
                Version = client.Version
            });
        // 名前変更コマンドを実行する
        WhenChange(client => new ChangeClientName(client.AggregateId, testClientChangedName) { ReferenceVersion = client.Version });
        // コマンドによって生成されたイベントを検証する
        ThenSingleEvent(client => new ClientNameChanged(client.AggregateId, testClientChangedName));
        // 現在の集約のステータスを検証する
        ThenState(
            client => new ClientDto
            {
                AggregateId = client.AggregateId,
                BranchId = branchDto.AggregateId,
                ClientEmail = testEmail,
                ClientName = testClientChangedName,
                Version = client.Version
            });
    }
    [Fact(DisplayName = "重複したメールアドレスが存在する場合、作成失敗する")]
    public void ClientCreateDuplicateEmailSpec()
    {
        var branchDto = new BranchDto { AggregateId = Guid.NewGuid(), Name = "TEST", Version = 1 };
        var clientDto = new ClientDto
        {
            AggregateId = Guid.NewGuid(),
            ClientName = "NOT DUPLICATED NAME",
            ClientEmail = testEmail,
            BranchId = Guid.NewGuid(),
            Version = 1
        };
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照ように渡す
        GivenEnvironmentDtos(new List<AggregateDtoBase> { branchDto, clientDto });
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
                client => new ClientDto
                {
                    AggregateId = client.AggregateId,
                    BranchId = branchId,
                    ClientEmail = testEmail,
                    ClientName = testClientName,
                    Version = client.Version
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
                client => new ClientDto
                {
                    AggregateId = client.AggregateId,
                    BranchId = branchId,
                    ClientEmail = testEmail,
                    ClientName = testClientChangedName,
                    Version = client.Version
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
                client => new ClientDto
                {
                    AggregateId = client.AggregateId,
                    BranchId = branchId,
                    ClientEmail = testEmail,
                    ClientName = testClientChangedNameV3,
                    Version = client.Version
                });
    }
    [Fact(DisplayName = "スナップショットを使用してテストを開始")]
    public void StartWithSnapshot()
    {
        var branchId = Guid.NewGuid();
        var clientId = Guid.NewGuid();

        Given(
                new ClientDto
                {
                    AggregateId = clientId,
                    BranchId = branchId,
                    ClientName = testClientName,
                    ClientEmail = testEmail,
                    Version = 1
                })
            .WhenMethod(client => client.ChangeClientName(testClientChangedName))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent(client => new ClientNameChanged(client.AggregateId, testClientChangedName))
            // 現在の集約のステータスを検証する
            .ThenState(
                client => new ClientDto
                {
                    AggregateId = client.AggregateId,
                    BranchId = branchId,
                    ClientEmail = testEmail,
                    ClientName = testClientChangedName,
                    Version = client.Version
                });
    }
}
