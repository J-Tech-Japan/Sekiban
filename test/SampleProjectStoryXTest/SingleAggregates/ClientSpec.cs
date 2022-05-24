using CustomerDomainContext.Aggregates.Branches;
using CustomerDomainContext.Aggregates.Clients;
using CustomerDomainContext.Aggregates.Clients.Commands;
using CustomerDomainContext.Aggregates.Clients.Events;
using Sekiban.EventSourcing.AggregateEvents;
using Sekiban.EventSourcing.Aggregates;
using Sekiban.EventSourcing.TestHelpers;
using System;
using System.Collections.Generic;
using Xunit;
namespace SampleProjectStoryXTest.SingleAggregates;

public class ClientSpec : SingleAggregateTestBase
{
    [Fact]
    public void ClientCreateSpec()
    {
        const string TestClientName = "TestName";
        const string TestClientChangedName = "TestName2";
        const string TestEmail = "test@example.com";
        var helper = new AggregateTestHelper<Client, ClientDto>(_serviceProvider);
        var branchDto = new BranchDto { AggregateId = Guid.NewGuid(), Name = "TEST", Version = 1 };
        // CreateコマンドでBranchを参照するため、BranchDtoオブジェクトを参照ように渡す
        helper.GivenSingleAggregateDtos(new List<AggregateDtoBase> { branchDto })
            // CreateClient コマンドを実行する
            .WhenCreate(new CreateClient(branchDto.AggregateId, TestClientName, TestEmail))
            // コマンドによって生成されたイベントを検証する
            .Then(
                (AggregateEvent ev) =>
                {
                    Assert.IsType<ClientCreated>(ev);
                    if (ev is ClientCreated clientCreated)
                    {
                        Assert.Equal(TestClientName, clientCreated.ClientName);
                        Assert.Equal(TestEmail, clientCreated.ClientEmail);
                    }
                })
            // 現在の集約のステータスを検証する
            .Expect(
                dto =>
                {
                    Assert.Equal(branchDto.AggregateId, dto.BranchId);
                    Assert.Equal(TestClientName, dto.ClientName);
                    Assert.Equal(TestEmail, dto.ClientEmail);
                })
            // 名前変更コマンドを実行する
            .WhenChange(new ChangeClientName(helper.Aggregate.AggregateId, TestClientChangedName) { ReferenceVersion = helper.Aggregate.Version })
            // コマンドによって生成されたイベントを検証する
            .Then(
                (AggregateEvent ev) =>
                {
                    Assert.IsType<ClientNameChanged>(ev);
                    if (ev is ClientNameChanged clientNameChanged)
                    {
                        Assert.Equal(helper.Aggregate.AggregateId, clientNameChanged.ClientId);
                        Assert.Equal(TestClientChangedName, clientNameChanged.ClientName);
                    }
                })
            // 現在の集約のステータスを検証する
            .Expect(
                dto =>
                {
                    Assert.Equal(branchDto.AggregateId, dto.BranchId);
                    Assert.Equal(TestClientChangedName, dto.ClientName);
                    Assert.Equal(TestEmail, dto.ClientEmail);
                });
    }
}
