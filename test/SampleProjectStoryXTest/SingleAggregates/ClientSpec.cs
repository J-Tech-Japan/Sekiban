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
    [Fact(DisplayName = "集約コマンドを実行してテストする")]
    public void ClientCreateSpec()
    {
        const string testClientName = "TestName";
        const string testClientChangedName = "TestName2";
        const string testEmail = "test@example.com";
        var branchDto = new BranchDto { AggregateId = Guid.NewGuid(), Name = "TEST", Version = 1 };
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照ように渡す
        GivenEnvironmentDtos(new List<AggregateDtoBase> { branchDto });
        // CreateClient コマンドを実行する
        WhenCreate(new CreateClient(branchDto.AggregateId, testClientName, testEmail));
        // コマンドによって生成されたイベントを検証する
        ThenSingleEvent<ClientCreated>(
            ev =>
            {
                Assert.Equal(testClientName, ev.ClientName);
                Assert.Equal(testEmail, ev.ClientEmail);
            });
        // 現在の集約のステータスを検証する
        Expect(
            dto =>
            {
                Assert.Equal(branchDto.AggregateId, dto.BranchId);
                Assert.Equal(testClientName, dto.ClientName);
                Assert.Equal(testEmail, dto.ClientEmail);
            });
        // 名前変更コマンドを実行する
        WhenChange(client => new ChangeClientName(client.AggregateId, testClientChangedName) { ReferenceVersion = client.Version });
        // コマンドによって生成されたイベントを検証する
        ThenSingleEvent<ClientNameChanged>(
            (ev, client) =>
            {
                Assert.Equal(client.AggregateId, ev.ClientId);
                Assert.Equal(testClientChangedName, ev.ClientName);
            });
        // 現在の集約のステータスを検証する
        Expect(
            dto =>
            {
                Assert.Equal(branchDto.AggregateId, dto.BranchId);
                Assert.Equal(testClientChangedName, dto.ClientName);
                Assert.Equal(testEmail, dto.ClientEmail);
            });
    }
    [Fact(DisplayName = "重複したメールアドレスが存在する場合、作成失敗する")]
    public void ClientCreateDuplicateEmailSpec()
    {
        const string testClientName = "TestName";
        const string testClientChangedName = "TestName2";
        const string testEmail = "test@example.com";
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
        Assert.Throws<SekibanEmailAlreadyRegistered>(() => WhenCreate(new CreateClient(branchDto.AggregateId, testClientName, testEmail)));
    }
    [Fact(DisplayName = "コマンドではなく、集約メソッドをテストする")]
    public void UsingAggregateFunctionNoCommand()
    {
        const string testClientName = "TestName";
        const string testClientChangedName = "TestName2";
        const string testEmail = "test@example.com";
        var branchId = Guid.NewGuid();

        WhenConstructor(new Client(branchId, testClientName, testEmail))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent<ClientCreated>(
                ev =>
                {
                    Assert.Equal(testClientName, ev.ClientName);
                    Assert.Equal(testEmail, ev.ClientEmail);
                })
            // 現在の集約のステータスを検証する
            .Expect(
                dto =>
                {
                    Assert.Equal(branchId, dto.BranchId);
                    Assert.Equal(testClientName, dto.ClientName);
                    Assert.Equal(testEmail, dto.ClientEmail);
                })
            .WhenMethod(
                aggregate =>
                {
                    aggregate.ChangeClientName(testClientChangedName);
                })
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent<ClientNameChanged>(
                (ev, client) =>
                {
                    Assert.Equal(client.AggregateId, ev.ClientId);
                    Assert.Equal(testClientChangedName, ev.ClientName);
                })
            // 現在の集約のステータスを検証する
            .Expect(
                dto =>
                {
                    Assert.Equal(branchId, dto.BranchId);
                    Assert.Equal(testClientChangedName, dto.ClientName);
                    Assert.Equal(testEmail, dto.ClientEmail);
                });
    }
    [Fact(DisplayName = "イベントを渡してスタートする")]
    public void StartWithEvents()
    {
        const string testClientName = "TestName";
        const string testClientChangedName = "TestName2";
        const string testClientChangedNameV3 = "TestName3";
        const string testEmail = "test@example.com";
        var branchId = Guid.NewGuid();
        Given(new ClientCreated(Guid.NewGuid(), branchId, testClientName, testEmail))
            .Given(client => new ClientNameChanged(client.AggregateId, testClientChangedName))
            .WhenMethod(client => client.ChangeClientName(testClientChangedNameV3))
            // コマンドによって生成されたイベントを検証する
            .ThenSingleEvent<ClientNameChanged>(
                (ev, client) =>
                {
                    Assert.Equal(client.AggregateId, ev.ClientId);
                    Assert.Equal(testClientChangedNameV3, ev.ClientName);
                })
            // 現在の集約のステータスを検証する
            .Expect(
                dto =>
                {
                    Assert.Equal(branchId, dto.BranchId);
                    Assert.Equal(testClientChangedNameV3, dto.ClientName);
                    Assert.Equal(testEmail, dto.ClientEmail);
                });
    }
    [Fact(DisplayName = "スナップショットを使用してテストを開始")]
    public void StartWithSnapshot()
    {
        const string testClientName = "TestName";
        const string testClientChangedName = "TestName2";
        const string testEmail = "test@example.com";
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
            .ThenSingleEvent<ClientNameChanged>(
                (ev, client) =>
                {
                    Assert.Equal(client.AggregateId, ev.ClientId);
                    Assert.Equal(testClientChangedName, ev.ClientName);
                })
            // 現在の集約のステータスを検証する
            .Expect(
                dto =>
                {
                    Assert.Equal(branchId, dto.BranchId);
                    Assert.Equal(testClientChangedName, dto.ClientName);
                    Assert.Equal(testEmail, dto.ClientEmail);
                });
    }
}
