namespace RenoDXCommander.Services;

/// <summary>
/// Defines the contract for extracting files from ReShade installer archives.
/// Supports both ZIP (older ReShade versions) and NSIS (ReShade 6.7.3+) formats
/// via System.IO.Compression and 7-Zip respectively.
/// </summary>
public interface ISevenZipExtractor
{
    /// <summary>
    /// Extracts a single file from the ReShade installer exe.
    /// Tries System.IO.Compression (ZIP) first, then 7-Zip for NSIS.
    /// </summary>
    /// <param name="exePath">Path to the ReShade installer executable.</param>
    /// <param name="entryName">Name of the file to extract from the archive.</param>
    /// <param name="outputPath">Destination path for the extracted file.</param>
    /// <exception cref="FileNotFoundException">
    /// Thrown when the installer is not found or the entry cannot be extracted.
    /// </exception>
    void ExtractFile(string exePath, string entryName, string outputPath);

    /// <summary>
    /// Finds 7z.exe on the system. Checks the bundled copy, common install locations, and PATH.
    /// </summary>
    /// <returns>Path to 7z.exe, or <c>null</c> if not found.</returns>
    string? Find7ZipExe();

    /// <summary>
    /// Lists all entry names found inside the archive (for diagnostics).
    /// </summary>
    /// <param name="exePath">Path to the archive to inspect.</param>
    /// <returns>A list of entry names prefixed with their source (ZIP or 7z).</returns>
    List<string> ListEntries(string exePath);
}
