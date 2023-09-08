using Sekiban.Core.Validation;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using Xunit;
namespace Sekiban.Test.CosmosDb.Validations;

public class ValidationTestWithPrimaryConstructor
{
    [Fact]
    public void Test1()
    {
        var m = new MemberWithPrimaryConstructor(
            "YAMADA Taro",
            25,
            "090-1111-2222",
            "hoge@example.com",
            null,
            ImmutableList<MemberWithPrimaryConstructor>.Empty);
        var vresults = m.ValidateProperties();
        Assert.False(vresults.Any());
        Assert.True(m.TryValidateProperties(out _));
    }

    [Fact]
    public void VerificationReturnsValidationErrorNameNotEntered()
    {
        var m = new MemberWithPrimaryConstructor(
            string.Empty,
            25,
            "090-1111-2222",
            "hoge@example.com",
            null,
            ImmutableList<MemberWithPrimaryConstructor>.Empty);
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact]
    public void TestNameDigitOverflow()
    {
        var m = new MemberWithPrimaryConstructor(
            "YAMADA Taroooooooooooooooo",
            25,
            "090-1111-2222",
            "hoge@example.com",
            null,
            ImmutableList<MemberWithPrimaryConstructor>.Empty);
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact]
    public void TestAgeOutsideNumericRange()
    {
        var m = new MemberWithPrimaryConstructor(
            "YAMADA Taro",
            80,
            "090-1111-2222",
            "hoge@example.com",
            null,
            ImmutableList<MemberWithPrimaryConstructor>.Empty);
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact]
    public void TestCharacterTypeOfPhoneNumber()
    {
        var m = new MemberWithPrimaryConstructor(
            "YAMADA Taro",
            25,
            "090-1111-abcd",
            "hoge@example.com",
            null,
            ImmutableList<MemberWithPrimaryConstructor>.Empty);
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact]
    public void TestEmailAddressFormat()
    {
        var m = new MemberWithPrimaryConstructor(
            "YAMADA Taro",
            25,
            "090-1111-2222",
            "hoge@example@com",
            null,
            ImmutableList<MemberWithPrimaryConstructor>.Empty);
        var vresults = m.ValidateProperties();
        Assert.True(vresults.Any());
    }

    [Fact]
    public void TestReferenceTypePropertyExists()
    {
        var m = new MemberWithPrimaryConstructor(
            "YAMADA Taro",
            25,
            "090-1111-2222",
            "hoge@example.com",
            new MemberWithPrimaryConstructor(
                "YAMADA Hanako",
                25,
                "080-1111-2222",
                "hana@example.com",
                null,
                ImmutableList<MemberWithPrimaryConstructor>.Empty),
            ImmutableList<MemberWithPrimaryConstructor>.Empty);
        var vresults = m.ValidateProperties();
        Assert.False(vresults.Any());
    }

    [Fact]
    public void TestPropertyOfReferenceTypeProperty()
    {
        var m = new MemberWithPrimaryConstructor(
            "YAMADA Taro",
            25,
            "090-1111-2222",
            "hoge@example.com",
            new MemberWithPrimaryConstructor(
                "YAMADA Hanakoooooooooooooo",
                25,
                "080-1111-2222",
                "hana@example.com",
                null,
                ImmutableList<MemberWithPrimaryConstructor>.Empty),
            ImmutableList<MemberWithPrimaryConstructor>.Empty);
        var vresults = m.ValidateProperties();
        var validationResults = vresults.ToList();
        Assert.True(validationResults.Any());
        Assert.Equal("Partner.Name", validationResults.First().MemberNames.First());
    }

    [Fact]
    public void TestArrayTypePropertyExists()
    {
        var m = new MemberWithPrimaryConstructor(
            "YAMADA Taro",
            25,
            "090-1111-2222",
            "hoge@example.com",
            new MemberWithPrimaryConstructor(
                "YAMADA Hanako",
                25,
                "080-1111-2222",
                "hana@example.com",
                null,
                ImmutableList<MemberWithPrimaryConstructor>.Empty),
            new List<MemberWithPrimaryConstructor>
            {
                new(
                    "SUZUKI Ichiro",
                    30,
                    null,
                    null,
                    null,
                    ImmutableList<MemberWithPrimaryConstructor>.Empty),
                new(
                    "Nakata Hidetoshi",
                    28,
                    null,
                    null,
                    null,
                    ImmutableList<MemberWithPrimaryConstructor>.Empty)
            }.ToImmutableList());
        var vresults = m.ValidateProperties();
        Assert.False(vresults.Any());
    }

    [Fact]
    public void TestFailsValidationInArrayType()
    {
        var m = new MemberWithPrimaryConstructor(
            "YAMADA Taro",
            25,
            "090-1111-2222",
            "hoge@example.com",
            new MemberWithPrimaryConstructor(
                "YAMADA Hanako",
                25,
                "080-1111-2222",
                "hana@example.com",
                null,
                ImmutableList<MemberWithPrimaryConstructor>.Empty),
            new List<MemberWithPrimaryConstructor>
            {
                new(
                    "SUZUKI Ichiro",
                    30,
                    null,
                    null,
                    null,
                    ImmutableList<MemberWithPrimaryConstructor>.Empty),
                new(
                    "Nakata Hidetoshi",
                    90,
                    null,
                    null,
                    null,
                    ImmutableList<MemberWithPrimaryConstructor>.Empty)
            }.ToImmutableList());
        var vresults = m.ValidateProperties();
        var validationResults = vresults.ToList();
        Assert.True(validationResults.Any());
        Assert.Equal("Friends[1].Age", validationResults.First().MemberNames.First());
        Debug.Assert(vresults != null, nameof(vresults) + " != null");
        Assert.Single(validationResults);
    }
}
