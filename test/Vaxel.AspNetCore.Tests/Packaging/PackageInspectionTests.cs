using System.IO.Compression;
using Xunit;

namespace Vaxel.AspNetCore.Tests.Packaging;

public sealed class PackageInspectionTests
{
    private static string GetArtifactsDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var artifactsDir = Path.Combine(dir.FullName, "artifacts", "nupkgs");
            if (Directory.Exists(artifactsDir))
            {
                return artifactsDir;
            }
            dir = dir.Parent;
        }

        // If not found, look relative to repo root
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return Path.Combine(repoRoot, "artifacts", "nupkgs");
    }

    [Fact]
    public void Pack_ProducesExactlyFourNupkgs_NoDatastar()
    {
        var nupkgDir = GetArtifactsDir();
        Assert.True(Directory.Exists(nupkgDir), $"Directory not found: {nupkgDir}");

        var files = Directory.GetFiles(nupkgDir, "*.nupkg").Select(Path.GetFileName).ToList();

        Assert.Contains("Vaxel.AspNetCore.1.0.0.nupkg", files);
        Assert.Contains("Vaxel.Client.1.0.0.nupkg", files);
        Assert.Contains("Vaxel.Testing.1.0.0.nupkg", files);
        Assert.Contains("Vaxel.Analyzers.1.0.0.nupkg", files);

        Assert.DoesNotContain(files, f => f!.Contains("Datastar", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, files.Count(f => f!.EndsWith(".nupkg")));
    }

    [Theory]
    [InlineData("Vaxel.AspNetCore.1.0.0.nupkg", "Vaxel.AspNetCore")]
    [InlineData("Vaxel.Client.1.0.0.nupkg", "Vaxel.Client")]
    [InlineData("Vaxel.Testing.1.0.0.nupkg", "Vaxel.Testing")]
    [InlineData("Vaxel.Analyzers.1.0.0.nupkg", "Vaxel.Analyzers")]
    public void Nupkg_ContainsReadme_AndPackageSpecificInfo(string packageFileName, string expectedId)
    {
        var packagePath = Path.Combine(GetArtifactsDir(), packageFileName);
        Assert.True(File.Exists(packagePath), $"Package not found: {packagePath}");

        using var zip = ZipFile.OpenRead(packagePath);

        // Assert README.md exists in zip
        var readmeEntry = zip.GetEntry("README.md");
        Assert.NotNull(readmeEntry);

        using var reader = new StreamReader(readmeEntry.Open());
        var content = reader.ReadToEnd();

        Assert.Contains(expectedId, content);
        Assert.Contains("https://github.com/System-D-AB/vaxel", content);
        Assert.DoesNotContain("npm install", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientNupkg_ContainsVaxelJs()
    {
        var packagePath = Path.Combine(GetArtifactsDir(), "Vaxel.Client.1.0.0.nupkg");
        Assert.True(File.Exists(packagePath), $"Package not found: {packagePath}");

        using var zip = ZipFile.OpenRead(packagePath);

        var vaxelJsEntry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("vaxel.js", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(vaxelJsEntry);
        Assert.True(vaxelJsEntry.Length > 1000, "vaxel.js payload should be populated");
    }

    [Fact]
    public void AnalyzersNupkg_ContainsAnalyzerDll()
    {
        var packagePath = Path.Combine(GetArtifactsDir(), "Vaxel.Analyzers.1.0.0.nupkg");
        Assert.True(File.Exists(packagePath), $"Package not found: {packagePath}");

        using var zip = ZipFile.OpenRead(packagePath);

        var analyzerEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("analyzers/dotnet/cs/Vaxel.Analyzers.dll", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(analyzerEntry);
    }
}
