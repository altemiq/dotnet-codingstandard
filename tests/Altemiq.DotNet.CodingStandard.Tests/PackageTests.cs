namespace Altemiq.DotNet.CodingStandard.Tests;

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Meziantou.Framework;
using Xunit.Abstractions;

public class PackageTests(PackageFixture fixture, ITestOutputHelper testOutputHelper) : IClassFixture<PackageFixture>
{
    [Fact]
    public async Task NoXmlDocumentationReported()
    {
        var project = new ProjectBuilder(fixture, testOutputHelper);
        project.AddCsprojFile();
        project.AddFile("sample.cs", """_ = System.DateTime.Now;""");
        var data = await project.BuildAndGetOutput();
        Assert.True(data.HasWarning("SA0001"));
    }

    private sealed class ProjectBuilder : IAsyncDisposable
    {
        private const string SarifFileName = "BuildOutput.sarif";

        private readonly TemporaryDirectory _directory;
        private readonly ITestOutputHelper _testOutputHelper;

        public ProjectBuilder(PackageFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;

            _directory = TemporaryDirectory.Create();
            _directory.CreateTextFile(
                "NuGet.config", 
                $"""
                <configuration>
                  <config>
                    <add key="globalPackagesFolder" value="{fixture.PackageDirectory}/packages" />
                  </config>
                  <packageSources>
                    <clear />
                    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                    <add key="TestSource" value="{fixture.PackageDirectory}" />
                  </packageSources>
                  <packageSourceMapping>
                    <packageSource key="nuget.org">
                        <package pattern="*" />
                    </packageSource>
                    <packageSource key="TestSource">
                        <package pattern="Altemiq.DotNet.CodingStandard" />
                    </packageSource>
                  </packageSourceMapping>
                </configuration>
                """);

            File.Copy(PathHelpers.GetRootDirectory() / "global.json", _directory.FullPath / "global.json");
        }

        public ProjectBuilder AddFile(string relativePath, string content)
        {
            File.WriteAllText(_directory.FullPath / relativePath, content);
            return this;
        }

        public ProjectBuilder AddCsprojFile((string Name, string Value)[]? properties = null, (string Name, string Version)[]? nuGetPackages = null, XElement[]? additionalProjectElements = null)
        {
            var propertiesElement = new XElement("PropertyGroup");
            if (properties is not null)
            {
                foreach (var (name, value) in properties)
                {
                    propertiesElement.Add(new XElement(name), value);
                }
            }

            var packagesElement = new XElement("ItemGroup");
            if (nuGetPackages is not null)
            {
                foreach (var package in nuGetPackages)
                {
                    packagesElement.Add(new XElement("PackageReference", new XAttribute("Include", package.Name), new XAttribute("Version", package.Version)));
                }
            }

            var content = $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>exe</OutputType>
                    <TargetFramework>net$(NETCoreAppMaximumVersion)</TargetFramework>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <Nullable>enable</Nullable>
                    <ErrorLog>{SarifFileName},version=2.1</ErrorLog>
                  </PropertyGroup>
                  {propertiesElement}
                  {packagesElement}
                  <ItemGroup>
                    <PackageReference Include="Altemiq.DotNet.CodingStandard" Version="999.9.9" />
                  </ItemGroup>
                  {string.Join(Environment.NewLine, additionalProjectElements?.Select(e => e.ToString()) ?? [])}
                </Project>
                """;

            File.WriteAllText(_directory.FullPath / "test.csproj", content);
            return this;
        }

        public async Task<BuildResult> BuildAndGetOutput(params string[]? buildArguments)
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = _directory.FullPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                ArgumentList = { "build" },
            };

            if (buildArguments is not null)
            {
                foreach (var arg in buildArguments)
                {
                    psi.ArgumentList.Add(arg);
                }
            }

            // Remove parent environment variables
            psi.Environment.Remove("CI");
            psi.Environment.Remove("GITHUB_ACTIONS");

            var result = await psi.RunAsTaskAsync();
            _testOutputHelper.WriteLine("Process exit code: " + result.ExitCode);
            _testOutputHelper.WriteLine(result.Output.ToString());

            var sarif = JsonSerializer.Deserialize<SarifFile>(File.ReadAllBytes(_directory.FullPath / SarifFileName));
            _testOutputHelper.WriteLine("Sarif result:\n" + string.Join("\n", sarif!.AllResults().Select(r => r.ToString())));
            return new BuildResult(result.ExitCode, result.Output, sarif);
        }

        public ValueTask DisposeAsync() => _directory.DisposeAsync();
    }

    private sealed record BuildResult(int ExitCode, ProcessOutputCollection ProcessOutput, SarifFile SarifFile)
    {
        public bool OutputContains(string value, StringComparison stringComparison = StringComparison.Ordinal) => ProcessOutput.Any(line => line.Text.Contains(value, stringComparison));
        public bool OutputDoesNotContain(string value, StringComparison stringComparison = StringComparison.Ordinal) => !ProcessOutput.Any(line => line.Text.Contains(value, stringComparison));
        public bool HasError() => SarifFile.AllResults().Any(r => r.Level == "error");
        public bool HasError(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "error" && r.RuleId == ruleId);
        public bool HasWarning() => SarifFile.AllResults().Any(r => r.Level == "warning");
        public bool HasWarning(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "warning" && r.RuleId == ruleId);
        public bool HasNote(string ruleId) => SarifFile.AllResults().Any(r => r.Level == "note" && r.RuleId == ruleId);
    }

    private sealed class SarifFile
    {
        [JsonPropertyName("runs")]
        public required SarifFileRun[] Runs { get; set; }

        public IEnumerable<SarifFileRunResult> AllResults() => Runs.SelectMany(r => r.Results);
    }

    private sealed class SarifFileRun
    {
        [JsonPropertyName("results")]
        public required SarifFileRunResult[] Results { get; set; }
    }

    private sealed class SarifFileRunResult
    {
        [JsonPropertyName("ruleId")]
        public required string RuleId { get; set; }

        [JsonPropertyName("level")]
        public required string Level { get; set; }

        [JsonPropertyName("message")]
        public required SarifFileRunResultMessage Message { get; set; }

        public override string ToString() => $"{Level}:{RuleId} {Message}";
    }

    private sealed class SarifFileRunResultMessage
    {
        [JsonPropertyName("text")]
        public required string Text { get; set; }

        public override string ToString() => Text;
    }
}
