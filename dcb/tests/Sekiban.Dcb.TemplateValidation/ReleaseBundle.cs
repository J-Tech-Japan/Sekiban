using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sekiban.Dcb.TemplateValidation;

internal sealed class ReleaseBundle
{
    private const string HostRepository = "J-Tech-Japan/SekibanIntentHost";
    private static readonly Regex Sha256 = new(
        "^[0-9a-f]{64}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex Commit = new(
        "^[0-9a-f]{40}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex CanonicalCheckRunsListing = new(
        "^commits/[0-9a-fA-F]{40}/check-runs\\?filter=all&per_page=100$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex UnfilteredCheckRunsListing = new(
        "^commits/[0-9a-fA-F]{40}/check-runs(\\?.*)?$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    private static readonly Regex ImmutableRef = new(
        "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[0-9a-f]{40}:.+$",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    // Unanchored scan for immutable references embedded anywhere in host
    // object bytes (for example inside a JSON string member).
    private static readonly Regex EmbeddedHostRef = new(
        "(?<![A-Za-z0-9_.-])J-Tech-Japan/SekibanIntentHost@([0-9a-fA-F]{40}):",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private ReleaseBundle(
        string recordPath,
        string hostRef,
        byte[] recordBytes,
        IReadOnlyDictionary<string, Entry> entries)
    {
        RecordPath = recordPath;
        HostRef = hostRef;
        RecordBytes = recordBytes;
        Entries = entries;
    }

    internal sealed record Entry(
        string Kind,
        string ImmutableRef,
        string Endpoint,
        string RelativePath,
        string Sha256,
        byte[] RawBytes,
        byte[]? ContentBytes);

    internal string RecordPath { get; }

    internal string HostRef { get; }

    internal byte[] RecordBytes { get; }

    internal IReadOnlyDictionary<string, Entry> Entries { get; }

    internal Entry GetEntry(string immutableRef)
    {
        Assert("bundle.missing-ref", Entries.TryGetValue(immutableRef, out var entry),
            $"Closed release bundle does not contain immutable reference {immutableRef}.");
        return entry!;
    }

    internal static ReleaseBundle Load(string bundleDirectory, string manifestPath)
    {
        bundleDirectory = Path.GetFullPath(bundleDirectory);
        manifestPath = Path.GetFullPath(manifestPath);
        Assert("bundle.location", Directory.Exists(bundleDirectory), $"Release bundle directory does not exist: {bundleDirectory}.");
        Assert("bundle.location", File.Exists(manifestPath), $"Release bundle manifest does not exist: {manifestPath}.");
        Assert("bundle.location", Path.GetDirectoryName(manifestPath) == bundleDirectory &&
               Path.GetFileName(manifestPath) == "bundle.json",
            "The manifest must be the bundle root's bundle.json file.");

        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = document.RootElement;
        Assert("bundle.manifest", root.ValueKind == JsonValueKind.Object, "Bundle manifest must be a JSON object.");
        RequireMembers(root, "bundle manifest", new[]
        {
            "schema_version", "host_repository", "host_ref", "record_relative_path", "entries"
        });
        Assert("bundle.manifest", GetInt(root, "schema_version") == 2, "Bundle manifest schema_version must be 2.");
        Assert("bundle.manifest", GetString(root, "host_repository") == HostRepository,
            "Bundle manifest host_repository must be the private canonical host.");
        var hostRef = GetString(root, "host_ref").ToLowerInvariant();
        Assert("bundle.manifest", Commit.IsMatch(hostRef), "Bundle manifest host_ref must be an immutable 40-character SHA.");

        var entries = root.GetProperty("entries");
        Assert("bundle.manifest", entries.ValueKind == JsonValueKind.Array && entries.GetArrayLength() > 0,
            "Bundle manifest entries must be a non-empty array.");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var refs = new HashSet<string>(StringComparer.Ordinal);
        var loaded = new Dictionary<string, Entry>(StringComparer.Ordinal);
        string? recordPath = null;
        byte[]? recordBytes = null;

        foreach (var entry in entries.EnumerateArray())
        {
            RequireMembers(entry, "bundle manifest entry", new[]
            {
                "kind", "immutable_ref", "endpoint", "relative_path", "sha256"
            });
            var kind = GetString(entry, "kind");
            var immutableRef = GetString(entry, "immutable_ref");
            var endpoint = GetString(entry, "endpoint");
            var relativePath = GetString(entry, "relative_path");
            var digest = GetString(entry, "sha256").ToLowerInvariant();
            Assert("bundle.entry-kind", kind is "record" or "host-response" or "github-response",
                $"Bundle manifest entry kind '{kind}' is not supported.");
            Assert("bundle.entry-ref", ImmutableRef.IsMatch(immutableRef),
                $"Bundle entry {relativePath} must identify a full immutable reference.");
            Assert("bundle.endpoint", endpoint == ExpectedEndpoint(immutableRef),
                $"Bundle entry {relativePath} endpoint does not match its immutable reference.");
            Assert("bundle.entry-digest", Sha256.IsMatch(digest), $"Bundle entry {relativePath} must have a SHA-256 digest.");
            Assert("bundle.path", IsSafeRelativePath(relativePath), $"Bundle entry path '{relativePath}' is unsafe.");
            Assert("bundle.alias", paths.Add(relativePath),
                $"Bundle aliases multiple immutable references to the same local path {relativePath}.");
            var expectedPathDigest = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes($"{immutableRef}\n{digest}"))).ToLowerInvariant();
            Assert("bundle.path", relativePath.StartsWith("objects/", StringComparison.Ordinal) &&
                   Path.GetFileNameWithoutExtension(relativePath) == expectedPathDigest,
                $"Bundle entry {relativePath} must be deterministically named by its immutable reference and content digest.");
            Assert("bundle.duplicate-ref", refs.Add(immutableRef),
                $"Bundle contains duplicate immutable reference {immutableRef}.");

            var fullPath = Path.GetFullPath(Path.Combine(bundleDirectory, relativePath));
            Assert("bundle.missing-file", IsWithin(bundleDirectory, fullPath) && File.Exists(fullPath),
                $"Bundle entry is missing: {relativePath}.");
            Assert("bundle.path", new FileInfo(fullPath).LinkTarget is null,
                $"Bundle entry must not be a symbolic link: {relativePath}.");
            var rawBytes = File.ReadAllBytes(fullPath);
            var actualDigest = Convert.ToHexString(SHA256.HashData(rawBytes)).ToLowerInvariant();
            Assert("bundle.entry-digest", actualDigest == digest, $"Bundle entry digest mismatch: {relativePath}.");

            byte[]? contentBytes;
            if (kind == "record")
            {
                Assert("bundle.record", recordPath is null, "Bundle manifest must contain exactly one record entry.");
                Assert("bundle.record", immutableRef.Contains($"@{hostRef}:", StringComparison.OrdinalIgnoreCase),
                    "The record entry must be anchored to the requested immutable host commit.");
                contentBytes = ValidateContentsEnvelope(rawBytes, immutableRef, relativePath);
                recordPath = fullPath;
                recordBytes = contentBytes;
            }
            else
            {
                contentBytes = ValidateResponseIdentity(rawBytes, immutableRef, relativePath);
            }

            loaded.Add(immutableRef, new Entry(
                kind, immutableRef, endpoint, relativePath, digest, rawBytes, contentBytes));
        }

        Assert("bundle.record", recordPath is not null && recordBytes is not null,
            "Bundle manifest must contain one record entry.");
        Assert("bundle.record", GetString(root, "record_relative_path") == Path.GetRelativePath(bundleDirectory, recordPath!),
            "Bundle record_relative_path must identify the record entry exactly.");

        var actualFiles = Directory.EnumerateFiles(bundleDirectory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(bundleDirectory, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path != "bundle.json")
            .ToHashSet(StringComparer.Ordinal);
        Assert("bundle.closed-files", actualFiles.SetEquals(paths),
            "Bundle contains an extra, missing, or unreachable file outside the closed manifest.");

        ValidateHostContentsEvidence(loaded);

        ValidateNoSelfContainingHostObjects(loaded);

        return new ReleaseBundle(recordPath!, hostRef, recordBytes!, loaded);
    }

    private static byte[]? ValidateResponseIdentity(byte[] bytes, string immutableRef, string relativePath)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var separator = immutableRef.IndexOf(':');
        var objectPath = immutableRef[(separator + 1)..];
        var expectedObjectId = objectPath.StartsWith("commits/", StringComparison.Ordinal) &&
                               objectPath["commits/".Length..].Length == 40
            ? objectPath["commits/".Length..]
            : objectPath.StartsWith("git/trees/", StringComparison.Ordinal) &&
              objectPath["git/trees/".Length..].Length == 40
                ? objectPath["git/trees/".Length..]
                : objectPath.StartsWith("git/blobs/", StringComparison.Ordinal) &&
                  objectPath["git/blobs/".Length..].Length == 40
                    ? objectPath["git/blobs/".Length..]
                    : null;
        if (expectedObjectId is not null)
        {
            Assert("bundle.response-identity", root.TryGetProperty("sha", out var sha) && sha.ValueKind == JsonValueKind.String &&
                   sha.GetString() == expectedObjectId,
                $"Bundle response {relativePath} is not the API object named by {immutableRef}.");
            return null;
        }

        if (objectPath.StartsWith("contents/", StringComparison.Ordinal))
        {
            return ValidateContentsEnvelope(bytes, immutableRef, relativePath);
        }

        Assert("bundle.response-identity", root.ValueKind == JsonValueKind.Object,
            $"Bundle response {relativePath} must be a JSON API object.");
        return null;
    }

    private static byte[] ValidateContentsEnvelope(byte[] bytes, string immutableRef, string relativePath)
    {
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var separator = immutableRef.IndexOf(':');
        var objectPath = immutableRef[(separator + 1)..];
        var expectedPath = objectPath.StartsWith("contents/", StringComparison.Ordinal)
            ? objectPath["contents/".Length..]
            : objectPath;
        Assert("bundle.contents-envelope", root.TryGetProperty("type", out var type) && type.GetString() == "file" &&
               root.TryGetProperty("encoding", out var encoding) && encoding.GetString() == "base64" &&
               root.TryGetProperty("path", out var path) && path.GetString() == expectedPath,
            $"Bundle response {relativePath} is not the immutable contents object named by {immutableRef}.");
        var encoded = GetString(root, "content").Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal);
        byte[] content;
        try
        {
            content = Convert.FromBase64String(encoded);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"[rule:bundle.contents-envelope] Bundle response {relativePath} contains invalid base64 content.", exception);
        }

        Assert("bundle.contents-blob", root.TryGetProperty("sha", out var sha) && sha.GetString() == GitBlobSha(content),
            $"Bundle response {relativePath} does not preserve the exact Git contents blob identity.");
        return content;
    }

    internal static string GitBlobSha(byte[] content)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = FindGitExecutable(),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("hash-object");
        startInfo.ArgumentList.Add("--stdin");
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git hash-object.");
        process.StandardInput.BaseStream.Write(content, 0, content.Length);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert("bundle.git", process.ExitCode == 0,
            $"git hash-object failed with exit code {process.ExitCode}: {error.Trim()}");
        var hash = output.Trim();
        Assert("bundle.git", Commit.IsMatch(hash), "git hash-object returned an invalid blob identity.");
        return hash;
    }

    private static string FindGitExecutable()
    {
        var executableName = OperatingSystem.IsWindows() ? "git.exe" : "git";
        var candidate = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => Path.Combine(directory, executableName))
            .FirstOrDefault(File.Exists);
        Assert("bundle.git", candidate is not null, "The git executable is not available on PATH.");
        return Path.GetFullPath(candidate!);
    }

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        !Path.IsPathRooted(path) &&
        path.Replace('\\', '/').Split('/').All(part => part is not "" and not "." and not "..");

    internal static string ExpectedEndpoint(string immutableRef)
    {
        var separator = immutableRef.IndexOf(':');
        var repositoryAndCommit = immutableRef[..separator];
        var objectPath = immutableRef[(separator + 1)..];
        var at = repositoryAndCommit.IndexOf('@');
        var repository = repositoryAndCommit[..at];
        var commit = repositoryAndCommit[(at + 1)..];
        return objectPath switch
        {
            _ when objectPath.StartsWith("contents/", StringComparison.Ordinal) =>
                $"repos/{repository}/{objectPath}?ref={commit}",
            // GitHub's commit check-runs listing defaults to filter=latest and
            // 30 items, which hides superseded same-name runs and truncates.
            // Only the fully filtered, single-page route is a valid reference.
            _ when CanonicalCheckRunsListing.IsMatch(objectPath) => $"repos/{repository}/{objectPath}",
            _ when UnfilteredCheckRunsListing.IsMatch(objectPath) =>
                $"invalid: commit check-runs listings must use {ClosedReleaseRecordValidator.CheckRunsListingRoute}",
            _ when objectPath.StartsWith("commits/", StringComparison.Ordinal) ||
                   objectPath.StartsWith("pulls/", StringComparison.Ordinal) ||
                   objectPath.StartsWith("actions/", StringComparison.Ordinal) ||
                   objectPath.StartsWith("check-runs/", StringComparison.Ordinal) ||
                   objectPath.StartsWith("issues/", StringComparison.Ordinal) ||
                   objectPath.StartsWith("releases/", StringComparison.Ordinal) ||
                   objectPath.StartsWith("compare/", StringComparison.Ordinal) =>
                $"repos/{repository}/{objectPath}",
            _ when objectPath.StartsWith("git/trees/", StringComparison.Ordinal) =>
                $"repos/{repository}/{objectPath}?recursive=1",
            _ when objectPath.StartsWith("git/", StringComparison.Ordinal) =>
                $"repos/{repository}/{objectPath}",
            _ => $"repos/{repository}/contents/{objectPath}?ref={commit}"
        };
    }

    private static string ReferenceCommit(string immutableRef)
    {
        var at = immutableRef.LastIndexOf('@');
        var colon = immutableRef.IndexOf(':', at + 1);
        Assert("bundle.entry-ref", at > 0 && colon > at, $"Immutable reference {immutableRef} has no commit identity.");
        return immutableRef[(at + 1)..colon];
    }

    internal static bool IsReaderAnchorReference(string immutableRef, string hostRef) =>
        immutableRef.StartsWith($"{HostRepository}@", StringComparison.OrdinalIgnoreCase) &&
        (immutableRef.Contains(":commits/", StringComparison.OrdinalIgnoreCase) ||
         immutableRef.Contains(":git/trees/", StringComparison.OrdinalIgnoreCase));

    private static void ValidateHostContentsEvidence(IReadOnlyDictionary<string, Entry> entries)
    {
        foreach (var contentEntry in entries.Values.Where(entry =>
                     entry.ContentBytes is not null &&
                     entry.ImmutableRef.Contains(":contents/", StringComparison.OrdinalIgnoreCase) &&
                     entry.ImmutableRef.StartsWith($"{HostRepository}@", StringComparison.OrdinalIgnoreCase)))
        {
            var commit = ReferenceCommit(contentEntry.ImmutableRef);
            var commitRef = $"{HostRepository}@{commit}:commits/{commit}";
            Assert("bundle.host-anchor", entries.TryGetValue(commitRef, out var commitEntry),
                $"Host contents object {contentEntry.ImmutableRef} is missing its immutable commit response.");
            using var commitDocument = JsonDocument.Parse(commitEntry!.RawBytes);
            var commitObject = commitDocument.RootElement;
            Assert("bundle.host-anchor", GetString(commitObject, "sha") == commit && commitObject.TryGetProperty("commit", out _),
                $"Host commit response is not bound to {commit}.");
            var commitDetails = commitObject.GetProperty("commit");
            var tree = commitDetails.GetProperty("tree");
            Assert("bundle.host-anchor", tree.ValueKind == JsonValueKind.Object, $"Host commit response is not bound to {commit}.");
            var treeSha = GetString(tree, "sha");
            var treeRef = $"{HostRepository}@{commit}:git/trees/{treeSha}";
            Assert("bundle.host-anchor", entries.TryGetValue(treeRef, out var treeEntry),
                $"Host contents object {contentEntry.ImmutableRef} is missing its immutable tree response.");
            using var treeDocument = JsonDocument.Parse(treeEntry!.RawBytes);
            var treeObject = treeDocument.RootElement;
            Assert("bundle.host-anchor", GetString(treeObject, "sha") == treeSha && treeObject.TryGetProperty("tree", out _),
                $"Host tree response is not bound to {treeSha}.");
            var treeEntries = treeObject.GetProperty("tree");
            Assert("bundle.host-anchor", treeEntries.ValueKind == JsonValueKind.Array, $"Host tree response is not bound to {treeSha}.");

            var separator = contentEntry.ImmutableRef.IndexOf(':');
            var expectedPath = contentEntry.ImmutableRef[(separator + 1)..]["contents/".Length..];
            var matches = treeEntries.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object &&
                               item.TryGetProperty("path", out var path) && path.GetString() == expectedPath)
                .ToArray();
            Assert("bundle.host-tree-blob", matches.Length == 1 && GetString(matches[0], "type") == "blob" &&
                   GetString(matches[0], "sha") == GitBlobSha(contentEntry.ContentBytes!),
                $"Host tree {treeSha} does not bind exactly one blob for {expectedPath}.");
        }
    }

    private static void ValidateNoSelfContainingHostObjects(IReadOnlyDictionary<string, Entry> entries)
    {
        foreach (var entry in entries.Values.Where(value =>
                     value.ContentBytes is not null &&
                     value.ImmutableRef.StartsWith($"{HostRepository}@", StringComparison.OrdinalIgnoreCase)))
        {
            var containingCommit = ReferenceCommit(entry.ImmutableRef);
            var text = Encoding.UTF8.GetString(entry.ContentBytes!);
            foreach (Match match in EmbeddedHostRef.Matches(text))
            {
                Assert("bundle.self-commit", !match.Groups[1].Value.Equals(containingCommit, StringComparison.OrdinalIgnoreCase),
                    $"Host object {entry.ImmutableRef} must not reference an object from its containing commit.");
            }
        }
    }

    private static bool IsWithin(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    private static void RequireMembers(JsonElement element, string path, IReadOnlyCollection<string> expected)
    {
        var names = expected.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            Assert("bundle.members", names.Contains(property.Name), $"{path} contains unknown member '{property.Name}'.");
            Assert("bundle.members", seen.Add(property.Name), $"{path} contains duplicate member '{property.Name}'.");
        }

        foreach (var name in expected)
        {
            Assert("bundle.members", element.TryGetProperty(name, out _), $"{path} is missing required member '{name}'.");
        }
    }

    private static string GetString(JsonElement element, string property)
    {
        Assert("bundle.members", element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String,
            $"Bundle property {property} is required and must be a string.");
        return value.GetString() ?? string.Empty;
    }

    private static int GetInt(JsonElement element, string property)
    {
        var result = 0;
        Assert("bundle.members", element.TryGetProperty(property, out var value) && value.TryGetInt32(out result),
            $"Bundle property {property} is required and must be an integer.");
        return result;
    }

    private static void Assert(string rule, bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"[rule:{rule}] {message}");
        }
    }
}
