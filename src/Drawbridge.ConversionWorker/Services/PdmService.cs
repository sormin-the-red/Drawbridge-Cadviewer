using Microsoft.Extensions.Logging;

namespace Drawbridge.ConversionWorker.Services
{
    public class PdmCheckoutResult
    {
        public List<string> SyncedFilePaths   { get; set; } = new();
        public string       AssemblyLocalPath { get; set; } = "";
    }

    public static class PdmService
    {
        public static PdmCheckoutResult CheckOutFile(
            string vaultName, string vaultFilePath, int version,
            string workDir, ILogger logger)
        {
            throw new NotImplementedException("PDM checkout not yet implemented");
        }

        public static string CheckOutSingleFile(
            string vaultName, string vaultFilePath, ILogger logger)
        {
            throw new NotImplementedException("PDM single-file checkout not yet implemented");
        }
    }
}
