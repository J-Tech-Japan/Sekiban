using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sekiban.Dcb.Tests.SerializedCommitWire;

/// <summary>
///     Prevents the EN/JA directional mapping table from drifting away from the frozen JSON member vocabulary. The scope
///     is intentionally the marked SEK-G52 table, not unrelated prose elsewhere in the serialization guide.
/// </summary>
public sealed class SerializedCommitInteropDocumentationTests
{
    private const string MappingStart = "<!-- SEK-G52-MAPPING-START -->";
    private const string MappingEnd = "<!-- SEK-G52-MAPPING-END -->";
    private static readonly Regex MemberName = new("`(?<name>[a-z][A-Za-z0-9]*)`", RegexOptions.CultureInvariant);

    [Fact]
    public void EnglishAndJapaneseMappingTables_ContainExactlyTheFrozenFixtureMemberVocabulary()
    {
        var root = SerializedCommitInteropPaths.FindRepositoryRoot();
        var fixtureMembers = ReadFixtureMemberVocabulary(Path.Combine(
            root,
            "dcb",
            "tests",
            "Sekiban.Dcb.WithResult.Tests",
            "SerializedCommitWire",
            "goldens"));
        var englishMembers = ReadMappingMembers(Path.Combine(root, "docs", "dcb_llm", "07_json_orleans_serialization.md"));
        var japaneseMembers = ReadMappingMembers(Path.Combine(root, "docs", "dcb_llm_ja", "07_json_orleans_serialization.md"));

        Assert.Equal(fixtureMembers.OrderBy(member => member), englishMembers.OrderBy(member => member));
        Assert.Equal(fixtureMembers.OrderBy(member => member), japaneseMembers.OrderBy(member => member));
    }

    private static HashSet<string> ReadFixtureMemberVocabulary(string goldensDirectory)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(goldensDirectory, "interop_*.json")
                     .Where(path => !path.EndsWith("interop_manifest.json", StringComparison.Ordinal)))
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            CollectPropertyNames(document.RootElement, members);
        }

        return members;
    }

    private static void CollectPropertyNames(JsonElement element, ISet<string> names)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    names.Add(property.Name);
                    // Payload contents are application-defined JSON values, not serialized-commit mapping members. Their
                    // intentional R1/R2/R3 edge shapes must not force the mapping table to list domain field names.
                    if (!string.Equals(property.Name, "payload", StringComparison.Ordinal))
                    {
                        CollectPropertyNames(property.Value, names);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectPropertyNames(item, names);
                }

                break;
        }
    }

    private static HashSet<string> ReadMappingMembers(string documentationPath)
    {
        var document = File.ReadAllText(documentationPath);
        var start = document.IndexOf(MappingStart, StringComparison.Ordinal);
        var end = document.IndexOf(MappingEnd, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"{documentationPath} is missing the SEK-G52 mapping markers.");

        var table = document[(start + MappingStart.Length)..end];
        return MemberName.Matches(table)
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }
}
