// Feature: downloads-folder-reorganisation, Property 2: Download path constants resolve under Root
using FsCheck;
using FsCheck.Xunit;
using RenoDXCommander.Services;

namespace RenoDXCommander.Tests;

/// <summary>
/// Property-based tests verifying that every path in
/// <see cref="DownloadPaths.AllSubdirectories"/> resolves under
/// <see cref="DownloadPaths.Root"/> and ends with the expected folder name.
/// </summary>
public class DownloadPathsResolveUnderRootPropertyTests
{
    /// <summary>
    /// Feature: downloads-folder-reorganisation, Property 2: Download path constants resolve under Root
    ///
    /// **Validates: Requirements 7.3**
    ///
    /// For any path in DownloadPaths.AllSubdirectories, that path shall start
    /// with DownloadPaths.Root as a prefix and shall end with the expected
    /// subdirectory name.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AllSubdirectories_ResolveUnder_Root()
    {
        var expectedFolderNames = new[] { "shaders", "renodx", "framelimiter", "misc", "luma" };

        var indexArb = Gen.Choose(0, DownloadPaths.AllSubdirectories.Length - 1).ToArbitrary();

        return Prop.ForAll(indexArb, (int index) =>
        {
            var path = DownloadPaths.AllSubdirectories[index];
            var expectedFolder = expectedFolderNames[index];

            var startsWithRoot = path.StartsWith(DownloadPaths.Root, StringComparison.OrdinalIgnoreCase);
            var endsWithFolder = Path.GetFileName(path)
                .Equals(expectedFolder, StringComparison.OrdinalIgnoreCase);

            return (startsWithRoot && endsWithFolder)
                .Label($"index={index}, path=\"{path}\", root=\"{DownloadPaths.Root}\", " +
                       $"expectedFolder=\"{expectedFolder}\", " +
                       $"startsWithRoot={startsWithRoot}, endsWithFolder={endsWithFolder}");
        });
    }
}
