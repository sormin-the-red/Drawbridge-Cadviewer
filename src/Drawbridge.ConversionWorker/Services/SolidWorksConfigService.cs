using Microsoft.Extensions.Logging;

namespace Drawbridge.ConversionWorker.Services
{
    public class SwConfigResult
    {
        public List<string>                        SucceededConfigs       { get; set; } = new();
        public Dictionary<string, List<string>>    SuppressedComponents   { get; set; } = new();
        public List<string>                        AllComponentLocalPaths { get; set; } = new();
    }

    public static class SolidWorksConfigService
    {
        public static SwConfigResult ProcessConfigs(
            string assemblyLocalPath, string[] configs,
            string vaultName, string vaultRootPath, ILogger logger)
        {
            throw new NotImplementedException("SolidWorks COM integration not yet implemented");
        }
    }
}
