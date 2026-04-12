using Microsoft.Extensions.Logging;
using System.IO.Compression;

namespace Drawbridge.ConversionWorker.Services
{
    /// <summary>
    /// Builds a zip archive from PDM-synced files, preserving vault-relative paths so
    /// SolidWorks reference resolution works when APS translates the assembly.
    ///
    /// Vault-relative path = local path with the vault root prefix stripped, e.g.
    ///   C:\CreativeWorks\Projects\PROD-0001\PROD-0001.SLDASM
    ///     → Projects/PROD-0001/PROD-0001.SLDASM  (entry name inside zip)
    /// </summary>
    public static class ZipPackService
    {
        // Returns the vault-relative path of the root assembly inside the zip
        // (forward-slash separated, as required by the APS rootFilename field).
        public static string CreateZip(
            IEnumerable<string> localFilePaths,
            string vaultRootPath,
            string assemblyLocalPath,
            string zipOutputPath,
            ILogger? logger = null)
        {
            var vaultRoot = Path.GetFullPath(vaultRootPath)
                               .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                            + Path.DirectorySeparatorChar;

            // .sldprt/.sldasm are already compressed binary formats; NoCompression avoids
            // wasting CPU for negligible size reduction.
            using var zip = ZipFile.Open(zipOutputPath, ZipArchiveMode.Create);

            int added = 0;
            string? assemblyEntryName = null;

            foreach (var localPath in localFilePaths)
            {
                if (!File.Exists(localPath))
                {
                    logger?.LogWarning("ZipPack: file not found, skipping: {Path}", localPath);
                    continue;
                }

                var entryName = ToEntryName(localPath, vaultRoot);
                var entry     = zip.CreateEntry(entryName, CompressionLevel.NoCompression);

                // FileShare.ReadWrite lets us read while SolidWorks has the file open.
                // CreateEntryFromFile uses FileShare.Read, which SW's exclusive lock blocks.
                using var src = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var dst = entry.Open();
                src.CopyTo(dst);
                added++;

                if (string.Equals(Path.GetFullPath(localPath),
                                  Path.GetFullPath(assemblyLocalPath),
                                  StringComparison.OrdinalIgnoreCase))
                {
                    assemblyEntryName = entryName;
                }
            }

            logger?.LogInformation("ZipPack: wrote {Count} file(s) → {Zip}", added, zipOutputPath);

            if (assemblyEntryName == null)
                throw new InvalidOperationException(
                    $"Assembly path was not in the file list: {assemblyLocalPath}");

            return assemblyEntryName;
        }

        private static string ToEntryName(string localPath, string vaultRoot)
        {
            var full = Path.GetFullPath(localPath);
            if (full.StartsWith(vaultRoot, StringComparison.OrdinalIgnoreCase))
                return full.Substring(vaultRoot.Length).Replace('\\', '/');

            return Path.GetFileName(localPath);
        }
    }
}
