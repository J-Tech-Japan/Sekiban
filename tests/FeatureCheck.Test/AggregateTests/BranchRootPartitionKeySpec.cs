using FeatureCheck.Domain.Aggregates.Branches;
using FeatureCheck.Domain.Aggregates.Branches.Commands;
using FeatureCheck.Domain.Shared;
using Sekiban.Core.Validation;
using Sekiban.Testing.SingleProjections;
using System;
using Xunit;
namespace FeatureCheck.Test.AggregateTests;

public class BranchRootPartitionKeySpec : AggregateTest<Branch, FeatureCheckDependency>
{
    [Theory]
    [InlineData("root-partition-key")]
    [InlineData("root_partition_key123")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789")]
    [InlineData("123456789012345678901234567890123456")]
    [InlineData("1")]
    public void CreateSuccess1(string rootPartitionKey)
    {
        var command = new CreateBranchWithRootPartitionKey { Name = "BranchName", RootPartitionKey = rootPartitionKey };
        WhenCommand(command);
        ThenPayloadIs(new Branch("BranchName", 0));
    }
    [Fact]
    public void CreateSuccessGuid()
    {
        var command = new CreateBranchWithRootPartitionKey
            { Name = "BranchName", RootPartitionKey = Guid.NewGuid().ToString() };
        WhenCommand(command);
        ThenPayloadIs(new Branch("BranchName", 0));
    }
    [Theory]
    [InlineData("")]
    [InlineData("=")]
    [InlineData("+")]
    [InlineData("[")]
    [InlineData("]")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("\\")]
    [InlineData("|")]
    [InlineData("(")]
    [InlineData(")")]
    [InlineData("?")]
    [InlineData("/")]
    [InlineData(",")]
    [InlineData(".")]
    [InlineData("<")]
    [InlineData(">")]
    [InlineData("日本語")]
    [InlineData("CAP")]
    [InlineData("!")]
    [InlineData("@")]
    [InlineData("#")]
    [InlineData("$")]
    [InlineData("%")]
    [InlineData("^")]
    [InlineData("&")]
    [InlineData("*")]
    [InlineData("1234567890123456789012345678901234567")]
    public void CreateWithRootPartitionKeyValidationErrors(string rootPartitionKey)
    {
        var command = new CreateBranchWithRootPartitionKey { Name = "BranchName", RootPartitionKey = rootPartitionKey };
        WhenCommand(command);
        ThenHasValidationErrors(
            new SekibanValidationParameterError[]
                { new("RootPartitionKey", new[] { "Root Partition Key only allow a-z, 0-9, -, _ and length 1-36" }) });
    }
}
