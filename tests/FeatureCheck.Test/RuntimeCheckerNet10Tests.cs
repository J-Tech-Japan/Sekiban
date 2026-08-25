using System;
using Sekiban.Web.Common;
using Xunit;

namespace FeatureCheck.Test;

public class RuntimeCheckerNet10Tests
{
    [Fact]
    public void IsDotNet9OrLater_UsesTheSupportedBranchOnNet10()
    {
        Assert.Equal(10, Environment.Version.Major);
        Assert.True(RuntimeChecker.IsDotNet9OrLater());
    }
}
