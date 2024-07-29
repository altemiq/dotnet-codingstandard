// -----------------------------------------------------------------------
// <copyright file="UpdateRestoreGraphTask.cs" company="Altemiq">
// Copyright (c) Altemiq. All rights reserved.
// </copyright>
// -----------------------------------------------------------------------

namespace Altemiq.DotNet.CodingStandard.Tasks;

using NuGet.Packaging.Core;

/// <summary>
/// Updates the restore graph with correct package IDs.
/// </summary>
public class UpdateRestoreGraphTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// The restore output path.
    /// </summary>
    [Microsoft.Build.Framework.Required]
    public string? ProjectAssetsFileAbsolutePath { get; set; }

    public override bool Execute()
    {
        var assetsFilePath = this.ProjectAssetsFileAbsolutePath;
        if (!File.Exists(assetsFilePath))
        {
            throw new PackagingException(NuGet.Common.NuGetLogCode.NU5023, string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                "The assets file produced by restore does not exist. Try restoring the project again. The expected location of the assets file is {0}.",
                assetsFilePath));
        }

        var lockFileFormat = new NuGet.ProjectModel.LockFileFormat();
        var assetsFile = lockFileFormat.Read(assetsFilePath);

        // get the projects for this
        if (assetsFile.PackageSpec is null)
        {
            throw new InvalidOperationException(string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                "The assets file found does not contain a valid package spec. Try restoring the project again. The location of the assets file is {0}.",
                assetsFilePath));
        }

        var projectDirectory = Path.GetDirectoryName(assetsFile.PackageSpec.RestoreMetadata.ProjectPath);

        var cache = new Dictionary<PackageIdentity, string>();
        var updated = false;
        foreach (var lockFileLibrary in assetsFile.Libraries)
        {
            if (lockFileLibrary.MSBuildProject is null)
            {
                continue;
            }

            var projectPathToLibrary = Path.GetFullPath(Path.Combine(projectDirectory, NuGet.Common.PathUtility.GetPathWithDirectorySeparator(lockFileLibrary.MSBuildProject)));

            var task = new Microsoft.Build.Tasks.MSBuild
            {
                BuildEngine = this.BuildEngine,
                Targets = ["_GetPackageId"],
                UseResultsCache = true,
                Projects = [new Microsoft.Build.Utilities.TaskItem(projectPathToLibrary)]
            };

            if (task.Execute())
            {
                var project = task.TargetOutputs[0];
                var packageId = project.GetMetadata("PackageId");

                if (packageId is not null && lockFileLibrary.Name != packageId)
                {
                    cache.Add(new PackageIdentity(lockFileLibrary.Name, lockFileLibrary.Version), packageId);
                    lockFileLibrary.Name = packageId;
                    updated = true;
                }
            }
        }

        foreach (var framework in assetsFile.PackageSpec.RestoreMetadata.TargetFrameworks)
        {
            var target = assetsFile.GetTarget(framework.FrameworkName, runtimeIdentifier: null);
            if (target is null)
            {
                continue;
            }

            var libraryIdentityToTargetLibrary = target
                .Libraries
                .ToLookup(library => new PackageIdentity(library.Name, library.Version));

            foreach (var grouping in libraryIdentityToTargetLibrary)
            {
                if (cache.TryGetValue(grouping.Key, out var packageId))
                {
                    foreach (var targetLibrary in grouping)
                    {
                        targetLibrary.Name = packageId;
                        updated = true;
                    }
                }
            }

            foreach (var projectReference in framework.ProjectReferences)
            {
            }
        }

        // write the assets back out
        if (updated)
        {
            File.WriteAllText(assetsFilePath, lockFileFormat.Render(assetsFile));
        }

        return true;
    }
}