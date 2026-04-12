using EPDM.Interop.epdm;
using Microsoft.Extensions.Logging;

namespace Drawbridge.ConversionWorker.Services
{
    public sealed class PdmCheckoutResult
    {
        // Local vault-cache path to the root assembly (.sldasm)
        public string AssemblyLocalPath { get; set; } = "";
        // Local paths for every synced file — used by ZipPackService to preserve vault-relative paths
        public List<string> SyncedFilePaths { get; set; } = new();
    }

    public static class PdmService
    {
        private static readonly string[] SwExtensions = { ".sldasm", ".sldprt", ".slddrw" };

        public static string CheckOutSingleFile(string vaultName, string vaultFilePath,
            ILogger? logger = null)
        {
            var vault = new EdmVault5();
            vault.LoginAuto(vaultName, 0);

            var file = vault.GetFileFromPath(vaultFilePath, out IEdmFolder5 folder);
            if (file == null)
                throw new InvalidOperationException($"File not found in vault: {vaultFilePath}");

            object versionObj = (object)0;
            object? pathObj   = null;
            file.GetFileCopy(0, ref versionObj, ref pathObj, 0, "");

            var localPath = file.GetLocalPath(folder.ID);
            if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
            {
                logger?.LogInformation("PdmService: synced single file '{Name}' → {Path}",
                    file.Name, localPath);
                return localPath;
            }

            var tempPath = pathObj?.ToString() ?? "";
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                return tempPath;

            throw new InvalidOperationException(
                $"File not in vault cache after sync: {vaultFilePath}");
        }

        public static PdmCheckoutResult CheckOutFile(string vaultName, string vaultFilePath,
            int version, string localWorkDir, ILogger? logger = null)
        {
            var vault = new EdmVault5();
            vault.LoginAuto(vaultName, 0);

            var file = vault.GetFileFromPath(vaultFilePath, out IEdmFolder5 folder);
            if (file == null)
                throw new InvalidOperationException($"File not found in vault: {vaultFilePath}");

            logger?.LogInformation("PdmService: found '{File}' in folder '{Folder}' (ID={FId})",
                file.Name, folder.Name, folder.ID);

            int synced = 0, alreadyPresent = 0, failed = 0;
            var visited   = new HashSet<int>();
            var filePaths = new List<string>();

            try
            {
                var refTree = file.GetReferenceTree(folder.ID, version);
                if (refTree != null)
                {
                    logger?.LogInformation("PdmService: walking reference tree...");
                    SyncRefTree(refTree, isRoot: true, visited, filePaths,
                        ref synced, ref alreadyPresent, ref failed, logger);
                }
                else
                {
                    logger?.LogWarning("PdmService: GetReferenceTree returned null");
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "PdmService: GetReferenceTree failed");
            }

            if (!visited.Contains(file.ID))
                SyncFile(file, folder, filePaths, ref synced, ref alreadyPresent, ref failed, logger);

            logger?.LogInformation(
                "PdmService: sync complete — synced={S} already-present={P} failed={F} total-paths={T}",
                synced, alreadyPresent, failed, filePaths.Count);

            var localPath = file.GetLocalPath(folder.ID);
            logger?.LogInformation("PdmService: assembly local path: '{Path}' exists={E}",
                localPath, !string.IsNullOrEmpty(localPath) && File.Exists(localPath));

            if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
                throw new InvalidOperationException(
                    $"Assembly not found in local vault cache after sync: {vaultFilePath}\n" +
                    $"  Local path : {localPath}\n" +
                    $"  Synced     : {synced}\n" +
                    $"  Present    : {alreadyPresent}\n" +
                    $"  Failed     : {failed}");

            if (!filePaths.Contains(localPath))
                filePaths.Add(localPath);

            return new PdmCheckoutResult
            {
                AssemblyLocalPath = localPath,
                SyncedFilePaths   = filePaths,
            };
        }

        private static void SyncRefTree(IEdmReference5 node, bool isRoot,
            HashSet<int> visited, List<string> filePaths,
            ref int synced, ref int alreadyPresent, ref int failed, ILogger? logger)
        {
            if (node == null) return;

            var nodeFile   = node.File;
            var nodeFolder = node.Folder;

            if (nodeFile != null && nodeFolder != null && visited.Add(nodeFile.ID))
                SyncFile(nodeFile, nodeFolder, filePaths,
                    ref synced, ref alreadyPresent, ref failed, logger);

            string projName = "";
            IEdmPos5 pos;
            try { pos = node.GetFirstChildPosition(ref projName, isRoot, true, 0); }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "PdmService: GetFirstChildPosition failed for '{Name}'",
                    nodeFile?.Name);
                return;
            }

            while (!pos.IsNull)
            {
                IEdmReference5 child;
                try { child = node.GetNextChild(pos); }
                catch { break; }

                if (child != null)
                    SyncRefTree(child, false, visited, filePaths,
                        ref synced, ref alreadyPresent, ref failed, logger);
            }
        }

        private static void SyncFile(IEdmFile5 file, IEdmFolder5 folder,
            List<string> filePaths,
            ref int synced, ref int alreadyPresent, ref int failed, ILogger? logger)
        {
            var ext = Path.GetExtension(file.Name).ToLower();
            if (Array.IndexOf(SwExtensions, ext) < 0) return;

            try
            {
                var localPath = file.GetLocalPath(folder.ID);
                logger?.LogInformation("PdmService: syncing '{Name}'...", file.Name);

                object versionObj = (object)0;
                object? pathObj   = null;
                file.GetFileCopy(0, ref versionObj, ref pathObj, 0, "");

                if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                {
                    logger?.LogInformation("PdmService: synced '{Name}' → {Path}",
                        file.Name, localPath);
                    filePaths.Add(localPath);
                    synced++;
                    return;
                }

                string tempPath = pathObj?.ToString() ?? "";
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    if (string.IsNullOrEmpty(localPath))
                    {
                        logger?.LogWarning(
                            "PdmService: no vault local path for '{Name}' — skipping", file.Name);
                        failed++;
                        return;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                    File.Copy(tempPath, localPath, overwrite: true);
                    logger?.LogInformation("PdmService: synced '{Name}' (via temp) → {Path}",
                        file.Name, localPath);
                    filePaths.Add(localPath);
                    synced++;
                }
                else
                {
                    logger?.LogWarning(
                        "PdmService: GetFileCopy produced nothing for '{Name}' " +
                        "(localPath='{L}', pathObj='{P}')",
                        file.Name, localPath, pathObj);
                    failed++;
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "PdmService: error syncing '{Name}'", file.Name);
                failed++;
            }
        }
    }
}
