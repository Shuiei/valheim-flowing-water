using System;
using System.IO;

namespace FlowingWater.Tests;

/// <summary>The repository the tests run from, for the tests that read its files.</summary>
internal static class Repository
{
    /// <summary>The repository's root: the first folder up from the tests that holds VERSION.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>A file of the repository, by its path from the root.</summary>
    public static string File(string path) => Path.Combine(Root, path);

    private static string FindRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            if (System.IO.File.Exists(Path.Combine(folder.FullName, "VERSION")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException($"No VERSION in any folder above {AppContext.BaseDirectory}: the tests run from outside the repository.");
    }
}
