sealed class CountCache
{
    public int? Value { get; set; }
    public DateTime? LastFetchedUtc { get; set; }
}