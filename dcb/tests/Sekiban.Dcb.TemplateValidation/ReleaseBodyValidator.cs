using System.Text.RegularExpressions;

namespace Sekiban.Dcb.TemplateValidation;

internal static class ReleaseBodyValidator
{
    private const string LibraryKind = "library";
    private const string JapaneseLanguage = "Japanese";
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    internal static void Validate(string repoRoot, string expectedVersion, string? bodyKind)
    {
        var releaseRoot = Path.Combine(repoRoot, "docs", "releases");
        Assert(Directory.Exists(releaseRoot), "docs/releases is required for the staged DCB release inputs.");
        var files = new[]
        {
            (Path.Combine(releaseRoot, $"dcb-v{expectedVersion}-library.en.md"), LibraryKind, "English"),
            (Path.Combine(releaseRoot, $"dcb-v{expectedVersion}-library.ja.md"), LibraryKind, JapaneseLanguage),
            (Path.Combine(releaseRoot, $"dcbTemplates-v{expectedVersion}.en.md"), "template", "English"),
            (Path.Combine(releaseRoot, $"dcbTemplates-v{expectedVersion}.ja.md"), "template", JapaneseLanguage)
        }.Where(file => bodyKind is null || string.Equals(file.Item2, bodyKind, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert(files.Length > 0, $"Unknown release-body kind '{bodyKind}'.");

        if (bodyKind is null)
        {
            var templateFiles = files.Where(f => f.Item2 == "template").ToArray();
            var count = templateFiles.Count(f => File.Exists(f.Item1));
            Assert(count is 0 or 2, "Template release bodies must be present as a complete EN/JA pair.");
            if (count == 0) files = files.Where(f => f.Item2 == LibraryKind).ToArray();
        }
        foreach (var (path, kind, language) in files)
        {
            Assert(File.Exists(path), $"Missing reviewed {language} {kind} release body: {path}");
            var content = File.ReadAllText(path);
            Assert(content.Contains(expectedVersion, StringComparison.Ordinal),
                $"{path} must name DCB {expectedVersion}.");
            Assert(!string.IsNullOrWhiteSpace(content), $"{path} must not be empty.");
            Assert(!content.Contains("TODO", StringComparison.OrdinalIgnoreCase) &&
                   !content.Contains("TBD", StringComparison.OrdinalIgnoreCase),
                $"{path} contains an unresolved release-body placeholder.");
            if (kind != LibraryKind)
                Assert(content.Contains(language == JapaneseLanguage ? "テンプレート" : "template", StringComparison.OrdinalIgnoreCase),
                    $"{path} must identify its template scope.");
            if (language == JapaneseLanguage)
                Assert(Regex.IsMatch(content, @"[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]", RegexOptions.None, RegexTimeout),
                    $"{path} must contain Japanese text.");
        }
    }

    private static void Assert(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
}
