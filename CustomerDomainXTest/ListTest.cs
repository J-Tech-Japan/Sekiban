using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Xunit;
namespace CustomerDomainXTest;

public class ListTest
{


    [Fact]
    public void AppendImmutableTest()
    {
        var immutable = ImmutableList<Record>.Empty;

        var second = immutable.Append(new Record("John", 20)).ToImmutableList();

        Assert.Empty(immutable);
        Assert.Single(second);

    }

    [Fact]
    public void AppendListTest()
    {
        var list = new List<Record>();

        var second = list.Append(new Record("John", 20)).ToList();

        Assert.Empty(list);
        Assert.Single(second);

    }

    public record Record(string Name, int Age);
}
