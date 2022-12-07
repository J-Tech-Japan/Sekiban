using System;
using Xunit;
using Xunit.Abstractions;
namespace SampleProjectStoryXTest;

public class TickTest
{
    private readonly ITestOutputHelper _testOutputHelper;
    public TickTest(ITestOutputHelper testOutputHelper) => _testOutputHelper = testOutputHelper;


    [Fact]
    public void CheckTickMinMax()
    {
        var tickmax = DateTime.MaxValue.Ticks.ToString();
        var tick2022 = new DateTime(2022, 1, 1).Ticks.ToString();
        var tickmin = DateTime.MinValue.Ticks.ToString();
        _testOutputHelper.WriteLine(tickmax);
        _testOutputHelper.WriteLine(tickmax.Length.ToString());
        _testOutputHelper.WriteLine(tick2022);
        _testOutputHelper.WriteLine(tick2022.Length.ToString());
        _testOutputHelper.WriteLine(tickmin);
        var tick2100 = new DateTime(2100, 1, 1).Ticks.ToString();
        _testOutputHelper.WriteLine(tick2100);
        _testOutputHelper.WriteLine(tick2100.Length.ToString());
        var tick3000 = new DateTime(3169, 11, 16).Ticks.ToString();
        _testOutputHelper.WriteLine(tick3000);
        _testOutputHelper.WriteLine(tick3000.Length.ToString());
        var tick19 = new DateTime(3169, 11, 17).Ticks.ToString();
        _testOutputHelper.WriteLine(tick19);
        _testOutputHelper.WriteLine(tick19.Length.ToString());
    }
}
