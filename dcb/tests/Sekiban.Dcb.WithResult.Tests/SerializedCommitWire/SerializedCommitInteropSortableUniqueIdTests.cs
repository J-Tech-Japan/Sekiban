using System.Text;
using Sekiban.Dcb.Common;

namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>SEK-G52 cross-runtime pin: JavaScript accepts exactly the C# 19-tick + 11-id decimal representation.</summary>
public sealed class SerializedCommitInteropSortableUniqueIdTests
{
    [Theory]
    [InlineData("000000000000000000000000000000")]
    [InlineData("063082281600000000000000000000")]
    [InlineData("315537897599999999999999999999")]
    public void FrozenVectors_ParseAsExactlyThirtyAsciiDecimalDigits(string value)
    {
        Assert.Equal(19, SortableUniqueId.TickNumberOfLength);
        Assert.Equal(11, SortableUniqueId.IdNumberOfLength);
        Assert.Equal(30, SortableUniqueId.TickNumberOfLength + SortableUniqueId.IdNumberOfLength);
        Assert.Equal(30, value.Length);
        Assert.Equal(30, Encoding.ASCII.GetByteCount(value));
        Assert.All(value, character => Assert.InRange(character, '0', '9'));

        var parsed = SortableUniqueId.Parse(value);
        Assert.True(parsed.IsSuccess, parsed.IsSuccess ? string.Empty : parsed.GetException().ToString());
        Assert.Equal(value, parsed.GetValue().Value);
    }

    [Fact]
    public void GenerationAndRoundTrip_KeepTheFrozenThirtyDigitWireShape()
    {
        var generated = SortableUniqueId.Generate(
            new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Guid.Empty);

        Assert.Equal("063082281600000000000000000000", generated);
        Assert.Equal(30, generated.Length);
        Assert.Equal(30, Encoding.ASCII.GetByteCount(generated));
        Assert.All(generated, character => Assert.InRange(character, '0', '9'));
        Assert.True(SortableUniqueId.TryParse(generated, out var parsed));
        Assert.NotNull(parsed);
        Assert.Equal(generated, parsed!.Value);
    }
}
