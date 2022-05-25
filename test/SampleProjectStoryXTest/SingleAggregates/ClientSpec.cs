using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Events;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
using System;
using System.Collections.Generic;
using Xunit;
namespace SampleProjectStoryXTest.SingleAggregates;

public class ClientSpec : SingleAggregateTestBase<Client, ClientDto>
{
    [Fact]
    public void ClientCreateSpec()
    {
        const string testClientName = "TestName";
        const string testClientChangedName = "TestName2";
        const string testEmail = "test@example.com";
        var branchDto = new BranchDto { AggregateId = Guid.NewGuid(), Name = "TEST", Version = 1 };
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照ように渡す
        _helper.GivenSingleAggregateDtos(new List<AggregateDtoBase> { branchDto })
            // CreateClient コマンドを実行する
            .WhenCreate(new CreateClient(branchDto.AggregateId, testClientName, testEmail))
            // コマンドによって生成されたイベントを検証する
            .Then(
                (AggregateEvent ev) =>
                {
                    Assert.IsType<ClientCreated>(ev);
                    if (ev is ClientCreated clientCreated)
                    {
                        Assert.Equal(testClientName, clientCreated.ClientName);
                        Assert.Equal(testEmail, clientCreated.ClientEmail);
                    }
                })
            // 現在の集約のステータスを検証する
            .Expect(
                dto =>
                {
                    Assert.Equal(branchDto.AggregateId, dto.BranchId);
                    Assert.Equal(testClientName, dto.ClientName);
                    Assert.Equal(testEmail, dto.ClientEmail);
                })
            // 名前変更コマンドを実行する
            .WhenChange(new ChangeClientName(_helper.Aggregate.AggregateId, testClientChangedName) { ReferenceVersion = _helper.Aggregate.Version })
            // コマンドによって生成されたイベントを検証する
            .Then(
                (AggregateEvent ev) =>
                {
                    Assert.IsType<ClientNameChanged>(ev);
                    if (ev is ClientNameChanged clientNameChanged)
                    {
                        Assert.Equal(_helper.Aggregate.AggregateId, clientNameChanged.ClientId);
                        Assert.Equal(testClientChangedName, clientNameChanged.ClientName);
                    }
                })
            // 現在の集約のステータスを検証する
            .Expect(
                dto =>
                {
                    Assert.Equal(branchDto.AggregateId, dto.BranchId);
                    Assert.Equal(testClientChangedName, dto.ClientName);
                    Assert.Equal(testEmail, dto.ClientEmail);
                });
    }
}
