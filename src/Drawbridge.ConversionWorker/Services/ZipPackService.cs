using Microsoft.Extensions.Logging;

namespace Drawbridge.ConversionWorker.Services
{
    public static class ZipPackService
    {
        // Zips all synced files preserving vault-relative paths and returns the
        // assembly's path relative to the zip root (used as APS rootFilename).
        public static string CreateZip(
            List<string> filePaths, string vaultRootPath,
            string assemblyLocalPath, string zipDestPath, ILogger logger)
        {
            throw new NotImplementedException("Zip packaging not yet implemented");
        }
    }
}
