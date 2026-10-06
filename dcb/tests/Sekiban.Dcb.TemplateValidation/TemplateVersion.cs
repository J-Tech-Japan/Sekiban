namespace Sekiban.Dcb.TemplateValidation;

internal static class TemplateVersion
{
    internal static string Read(string repoRoot)
    {
        repoRoot = Path.GetFullPath(repoRoot);
        return ReleaseProcess.Run("bash", repoRoot,
            Path.Combine(repoRoot, "dcb/tests/Sekiban.Dcb.TemplateValidation/read-template-version.sh"),
            "--repo-root", repoRoot).Trim();
    }
}
