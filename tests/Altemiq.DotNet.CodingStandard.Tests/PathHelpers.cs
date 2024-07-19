namespace Altemiq.DotNet.CodingStandard.Tests;

using Meziantou.Framework;

internal static class PathHelpers
{
    public static FullPath GetRootDirectory()
    {
        FullPath directory = FullPath.CurrentDirectory();
        while (!Directory.Exists(directory / ".git"))
        {
            directory = directory.Parent;
        }

        return directory;
    }
}
