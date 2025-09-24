using ResultBoxes;
namespace Sekiban.Dcb.Orleans.Tests;

public static class OptionalDateFixtures
{
    public static readonly DateOnly ExpectedDate = new(2024, 1, 23);

    public static readonly IReadOnlyList<OptionalDateResult> SeedResults = new[]
    {
        new OptionalDateResult(new OptionalValue<DateOnly>(ExpectedDate)),
        new OptionalDateResult(OptionalValue<DateOnly>.Empty)
    };
}