using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Sekiban.Dcb.TemplateValidation;

// API access is injected; production uses read-only gh api requests with GH_TOKEN.
internal interface IReleaseApi
{
    JsonElement Get(string endpoint);
}

internal sealed class GitHubReleaseApi : IReleaseApi
{
    public JsonElement Get(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GH_TOKEN")))
            throw new InvalidOperationException("GH_TOKEN must be the workflow token for live run/job reads.");
        using var document = JsonDocument.Parse(ReleaseProcess.Run("gh", null, "api", "--method", "GET", endpoint));
        return document.RootElement.Clone();
    }
}

internal static class ReleaseProcess
{
    internal static string Run(string command, string? cwd, params string[] args)
    {
        var start = new ProcessStartInfo(command) { RedirectStandardOutput = true, RedirectStandardError = true };
        if (cwd is not null) start.WorkingDirectory = cwd;
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException($"Cannot start {command}.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{command} failed: {stderr.Result.Trim()}");
        return stdout.Result;
    }
}

internal static class Schema3ReleaseRecordValidator
{
    private const string Repository = "J-Tech-Japan/Sekiban";
    private const string Host = "J-Tech-Japan/SekibanIntentHost";
    internal static readonly (string Alias, string Workflow, string Job)[] Checks =
    [
        ("dcbTestsNet9", "run_test_dcb.yml", "dcbTestsNet9"),
        ("dcbTestsNet10", "run_test_dcb.yml", "dcbTestsNet10"),
        ("packagedConsumer", "dcb_azure_queue_packaged_consumer.yml", "packaged-consumer"),
        ("templateConsumer", "dcb_template_validation.yml", "Pack, install, generate, restore, build, and test templates")
    ];

    internal static string Validate(string bundle, string manifest, string repoRoot, string version, string state, IReleaseApi api)
    {
        Require(Regex.IsMatch(version, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"), "Invalid release version.");
        Require(state is "prepared" or "complete", "Stage must be prepared or complete.");
        Require(Path.GetFullPath(manifest) == Path.Combine(Path.GetFullPath(bundle), "bundle.json"), "Detached manifest is not accepted.");
        var index = Read(manifest);
        Members(index, "host_repository", "host_ref", "record_path", "record_blob_sha");
        var hostRef = Text(index, "host_ref");
        Require(Hex(hostRef, 40) && Text(index, "host_repository") == Host, "Invalid immutable host source.");
        var path = $"intents/sekiban/releases/dcb-v{version}-release-record.json";
        Require(Text(index, "record_path") == path, "Record path must be version-derived.");
        var bytes = File.ReadAllBytes(Path.Combine(bundle, "record.json"));
        var blob = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes($"blob {bytes.Length}\0").Concat(bytes).ToArray()));
        Require(blob == Text(index, "record_blob_sha"), "Record bytes differ from the host blob.");
        var commit = Read(Path.Combine(bundle, "commit.json"));
        Require(Text(commit, "sha") == hostRef, "Host commit is not the requested ref.");
        var treeSha = Text(commit.GetProperty("commit").GetProperty("tree"), "sha");
        Require(Hex(treeSha, 40), "Invalid host tree SHA.");
        var tree = Read(Path.Combine(bundle, "tree.json"));
        Require(Text(tree, "sha") == treeSha && tree.GetProperty("truncated").ValueKind == JsonValueKind.False, "Host tree is mismatched or truncated.");
        var matches = tree.GetProperty("tree").EnumerateArray().Where(e => Text(e, "path") == path).ToArray();
        Require(matches.Length == 1 && Text(matches[0], "type") == "blob" && Text(matches[0], "sha") == blob, "Host tree does not bind the record blob.");
        using var document = JsonDocument.Parse(bytes);
        var record = document.RootElement;
        Members(record, state == "complete"
            ? ["schema_version", "version", "stage", "merged_sha", "candidate_pr", "checks", "release_bodies", "published"]
            : ["schema_version", "version", "stage", "merged_sha", "candidate_pr", "checks", "release_bodies"]);
        Require(record.GetProperty("schema_version").GetInt32() == 3, "Only schema 3 is supported.");
        Require(Text(record, "version") == version && Text(record, "stage") == state, "Wrong record version or stage.");
        Require(record.GetProperty("candidate_pr").GetInt64() >= 0, "candidate_pr must be a non-negative PR number.");
        var merged = Text(record, "merged_sha");
        Require(Hex(merged, 40), "Invalid merged_sha.");
        Require(ReleaseProcess.Run("git", repoRoot, "rev-parse", "HEAD^{commit}").Trim() == merged, "merged_sha does not equal checked-out HEAD.");
        // A full checkout/fetch of main is mandatory. merge-base accepts any ancestor, not only its tip.
        ReleaseProcess.Run("git", repoRoot, "merge-base", "--is-ancestor", merged, "origin/main");
        var recordedChecks = record.GetProperty("checks").EnumerateArray().ToArray();
        Require(recordedChecks.Length == Checks.Length, "Exactly four check aliases are required.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var check in recordedChecks)
        {
            Members(check, "name", "run_id");
            var name = Text(check, "name");
            Require(names.Add(name) && Checks.Any(c => c.Alias == name), "Missing, duplicate or unknown check alias.");
            var runId = check.GetProperty("run_id").GetInt64();
            Require(runId > 0, "run_id must be positive.");
            var mapping = Checks.Single(c => c.Alias == name);
            var endpoint = $"repos/{Repository}/actions/runs/{runId}";
            var run = api.Get(endpoint);
            var attempt = ValidateRun(run, runId, merged, mapping.Workflow);
            var jobs = new List<JsonElement>();
            for (var page = 1; ; page++)
            {
                var result = api.Get($"{endpoint}/attempts/{attempt}/jobs?per_page=100&page={page}");
                var current = result.GetProperty("jobs").EnumerateArray().ToArray();
                jobs.AddRange(current);
                if (jobs.Count >= result.GetProperty("total_count").GetInt32()) break;
                Require(current.Length > 0, "Incomplete job pagination.");
            }
            var matched = jobs.Where(j => Text(j, "name") == mapping.Job).ToArray();
            Require(matched.Length == 1, $"Expected exactly one job for alias {name}.");
            var job = matched[0];
            Require(job.GetProperty("run_id").GetInt64() == runId && job.GetProperty("run_attempt").GetInt32() == attempt &&
                Text(job, "head_sha") == merged && Text(job, "status") == "completed" && Text(job, "conclusion") == "success",
                $"Latest attempt job {mapping.Job} is not successful on merged_sha.");
            // Reject a re-run starting while the job list is being read.
            Require(ValidateRun(api.Get(endpoint), runId, merged, mapping.Workflow) == attempt, "Run attempt changed during validation.");
        }
        var bodies = record.GetProperty("release_bodies");
        Members(bodies, "library_en_sha256", "library_ja_sha256", "template_en_sha256", "template_ja_sha256");
        foreach (var (key, file) in new[] {
            ("library_en_sha256", $"dcb-v{version}-library.en.md"), ("library_ja_sha256", $"dcb-v{version}-library.ja.md"),
            ("template_en_sha256", $"dcbTemplates-v{version}.en.md"), ("template_ja_sha256", $"dcbTemplates-v{version}.ja.md") })
        {
            var hash = Text(bodies, key);
            Require(Hex(hash, 64) && hash == Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(repoRoot, "docs/releases", file)))), $"Body hash mismatch: {key}.");
        }
        ReleaseBodyValidator.Validate(repoRoot, version, null);
        if (state == "complete")
        {
            var published = record.GetProperty("published");
            Members(published, "library_release_url", "template_release_url");
            foreach (var (tag, key) in new[] { ($"dcb-v{version}", "library_release_url"), ($"dcbTemplates-v{version}", "template_release_url") })
            {
                var reference = api.Get($"repos/{Repository}/git/ref/tags/{tag}");
                var obj = reference.GetProperty("object");
                var peeled = Text(obj, "sha");
                var seen = new HashSet<string>();
                while (Text(obj, "type") == "tag")
                {
                    Require(seen.Add(peeled), "Cyclic tag objects.");
                    obj = api.Get($"repos/{Repository}/git/tags/{peeled}").GetProperty("object");
                    peeled = Text(obj, "sha");
                }
                Require(Text(obj, "type") == "commit" && peeled == merged, $"{tag} does not peel to merged_sha.");
                var url = $"https://github.com/{Repository}/releases/tag/{tag}";
                var release = api.Get($"repos/{Repository}/releases/tags/{tag}");
                Require(Text(published, key) == url && Text(release, "html_url") == url && Text(release, "tag_name") == tag &&
                    release.GetProperty("draft").ValueKind == JsonValueKind.False, $"{tag} release URL is not a non-draft release for the tag.");
            }
        }
        return merged;
    }

    private static int ValidateRun(JsonElement run, long id, string merged, string workflow)
    {
        Require(run.GetProperty("id").GetInt64() == id && Text(run.GetProperty("repository"), "full_name") == Repository &&
            Text(run.GetProperty("head_repository"), "full_name") == Repository, "Run repository or head repository is wrong.");
        Require(Text(run, "path") == $".github/workflows/{workflow}", "Wrong workflow for check alias.");
        Require(Text(run, "event") == "workflow_dispatch" && Text(run, "head_sha") == merged, "Run event or head_sha is wrong.");
        Require(Text(run, "status") == "completed" && Text(run, "conclusion") == "success", "Latest run attempt is not completed and successful.");
        var attempt = run.GetProperty("run_attempt").GetInt32();
        Require(attempt > 0, "Invalid run attempt.");
        return attempt;
    }

    private static JsonElement Read(string path) { using var d = JsonDocument.Parse(File.ReadAllBytes(path)); return d.RootElement.Clone(); }
    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString() ?? throw new InvalidOperationException($"Missing {key}.");
    private static bool Hex(string value, int length) => value.Length == length && value.All(Uri.IsHexDigit);
    private static void Members(JsonElement value, params string[] names)
    {
        var actual = value.EnumerateObject().Select(p => p.Name).ToArray();
        Require(actual.Length == names.Length && new HashSet<string>(actual).SetEquals(names), "Unexpected, missing or duplicate schema members.");
    }
    private static void Require(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
}
