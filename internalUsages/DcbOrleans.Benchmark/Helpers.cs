static class Helpers
{
    public static async Task<string> SafeReadAsync(HttpResponseMessage res, CancellationToken ct)
    {
        try { return await res.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }
    public static string Trim(string s)
        => string.IsNullOrEmpty(s) ? s : (s.Length > 256 ? s.Substring(0, 256) + "..." : s);
}