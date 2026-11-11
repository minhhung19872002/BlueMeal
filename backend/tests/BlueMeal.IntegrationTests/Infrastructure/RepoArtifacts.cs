namespace BlueMeal.IntegrationTests.Infrastructure;

/// <summary>
/// Some tests regenerate files that are committed to the repo (docs/api/openapi.json, tools/import-templates/*.xlsx).
/// A plain <c>dotnet test</c> only checks them; writing happens only when BLUEMEAL_EXPORT_ARTIFACTS=1, so running the
/// suite never leaves a dirty working tree. Regenerate with: <c>BLUEMEAL_EXPORT_ARTIFACTS=1 dotnet test --filter Export</c>.
/// </summary>
public static class RepoArtifacts
{
    public const string EnvVar = "BLUEMEAL_EXPORT_ARTIFACTS";

    /// <summary>Repository directory (relative path under the repo root) to write into, or null when exporting is off.</summary>
    public static string? Directory(params string[] relative)
    {
        if (Environment.GetEnvironmentVariable(EnvVar) != "1") return null;
        var root = FindRepoRoot();
        if (root is null) return null;
        var dir = Path.Combine([root, .. relative]);
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private static string? FindRepoRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (System.IO.Directory.Exists(Path.Combine(d.FullName, ".git")) || File.Exists(Path.Combine(d.FullName, "SPEC.md"))) return d.FullName;
        return null;
    }
}
