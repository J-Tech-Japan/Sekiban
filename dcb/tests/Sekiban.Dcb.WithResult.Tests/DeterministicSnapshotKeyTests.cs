using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Tests;

public class DeterministicSnapshotKeyTests
{
    private static readonly string Hash = new('a', 64);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NoPrefix_PreservesExistingKey_WithoutLengthCheck(string? prefix)
    {
        var projector = new string('p', 513);
        Assert.Equal(StreamOffloadHelper.ComputeDeterministicKey(projector, Hash),
            StreamOffloadHelper.ComputeDeterministicKey(prefix, projector, Hash));
    }

    [Theory]
    [InlineData("tenant", "projector", "tenant/projector")]
    [InlineData("tenant///", "projector", "tenant/projector")]
    [InlineData("tenant", "/folder/projector/", "tenant/folder/projector")]
    [InlineData("tenant", "\\folder\\projector\\", "tenant/folder/projector")]
    [InlineData("tenant", "projector/v1", "tenant/projector/v1")]
    [InlineData("/tenant\\nested", "projector", "/tenant\\nested/projector")]
    [InlineData("///", "projector", "/projector")]
    [InlineData(" ", "projector", " /projector")]
    public void Prefix_IsLiteralApartFromTrailingSlashes(string prefix, string projector, string folder)
    {
        Assert.Equal($"{folder}/{Hash}.bin",
            StreamOffloadHelper.ComputeDeterministicKey(prefix, projector, Hash));
    }

    [Fact]
    public void PrefixedKey_AtLimit_IsAccepted()
    {
        Assert.Equal(512, StreamOffloadHelper.MaxDeterministicKeyLength);
        var key = StreamOffloadHelper.ComputeDeterministicKey(new string('t', 57),
            $"{new string('p', 256)}/{new string('v', 128)}", Hash);
        Assert.Equal(StreamOffloadHelper.MaxDeterministicKeyLength, key.Length);
    }

    [Fact]
    public void PrefixedKey_OverLimit_ThrowsWithPrefixLengthAndLimit()
    {
        var prefix = new string('t', 58);
        var error = Assert.Throws<InvalidOperationException>(() =>
            StreamOffloadHelper.ComputeDeterministicKey(prefix,
                $"{new string('p', 256)}/{new string('v', 128)}", Hash));
        Assert.Contains(prefix, error.Message);
        Assert.Contains("513", error.Message);
        Assert.Contains("512", error.Message);
    }
}
