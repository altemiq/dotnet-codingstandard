namespace Altemiq.DotNet.CodingStandard.Tests;

using Meziantou.Framework;

public sealed class PackageFixture : IAsyncLifetime
{
    private readonly TemporaryDirectory _packageDirectory = TemporaryDirectory.Create();

    public FullPath PackageDirectory => _packageDirectory.FullPath;

    public async Task InitializeAsync()
    {
        // Build NuGet package
        FullPath nugetPath = FullPath.GetTempPath() / $"nuget-{Guid.NewGuid()}.exe";
        await DownloadFileAsync("https://dist.nuget.org/win-x86-commandline/latest/nuget.exe", nugetPath);
        FullPath nuspecPath = PathHelpers.GetRootDirectory() / "Altemiq.DotNet.CodingStandard.nuspec";

        System.Diagnostics.ProcessStartInfo psi = new(nugetPath);
        psi.ArgumentList.AddRange(["pack", nuspecPath, "-ForceEnglishOutput", "-Version", "999.9.9", "-OutputDirectory", _packageDirectory.FullPath]);
        _ = await psi.RunAsTaskAsync();
    }

    public async Task DisposeAsync()
    {
        await _packageDirectory.DisposeAsync();
    }

    private static async Task DownloadFileAsync(string url, FullPath path)
    {
        path.CreateParentDirectory();
        await using Stream nugetStream = await SharedHttpClient.Instance.GetStreamAsync(url);
        await using FileStream fileStream = File.Create(path);
        await nugetStream.CopyToAsync(fileStream);
    }
}
