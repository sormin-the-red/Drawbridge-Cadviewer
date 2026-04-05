using EPDM.Interop.epdm;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

// P/Invoke shims for COM ROT access (Marshal.GetActiveObject removed in .NET 5+)
internal static class ComRot
{
    [DllImport("ole32.dll")]
    private static extern int CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

    [DllImport("oleaut32.dll")]
    private static extern int GetActiveObject(ref Guid rclsid, IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    public static object GetActiveObject(string progId)
    {
        CLSIDFromProgID(progId, out var clsid);
        int hr = GetActiveObject(ref clsid, IntPtr.Zero, out var obj);
        Marshal.ThrowExceptionForHR(hr);
        return obj;
    }
}

namespace Drawbridge.ConversionWorker.Services
{
    public class ConfigProcessResult
    {
        public IReadOnlyList<string> SucceededConfigs { get; init; } = Array.Empty<string>();
        // Maps canonical config name → file names (no extension) of suppressed top-level components
        public IReadOnlyDictionary<string, List<string>> SuppressedComponents { get; init; } =
            new Dictionary<string, List<string>>();
        // Local vault-cache paths for every component SW found across all configs — may include
        // paths that PDM's reference tree missed when indexed under a partial configuration
        public IReadOnlyList<string> AllComponentLocalPaths { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// SolidWorks COM integration — opens an assembly once, activates each requested
    /// configuration in sequence, saves after each, then closes the session.
    ///
    /// All COM calls run on a dedicated STA thread. Uses Type.InvokeMember throughout —
    /// the C# dynamic binder triggers IDispatchComObject.EnsureScanDefinedMethods →
    /// GetITypeInfoFromIDispatch on every member access, which throws TYPE_E_ELEMENTNOTFOUND
    /// for SolidWorks on .NET 6+. InvokeMember calls IDispatch.GetIDsOfNames + Invoke directly.
    /// </summary>
    public static class SolidWorksConfigService
    {
        // SolidWorks 2023 = version 31
        private const string SwProgId = "SldWorks.Application.31";

        // swDocumentTypes_e.swDocASSEMBLY
        private const int SwDocAssembly = 2;
        // swOpenDocOptions_e.swOpenDocOptions_Silent | swOpenDocOptions_LoadModel
        private const int SwOpenOptions = 1 | 16;

        public static ConfigProcessResult ProcessConfigs(
            string assemblyPath,
            string[] configNames,
            string vaultName,
            string vaultRootPath,
            ILogger logger)
        {
            var succeeded          = new List<string>();
            var suppressedByConfig = new Dictionary<string, List<string>>();
            var allComponentPaths  = new List<string>();
            Exception? caught      = null;

            var timeout = TimeSpan.FromMinutes(15 + configNames.Length * 20);
            var thread  = new Thread(() =>
            {
                try
                {
                    ProcessConfigsInternal(assemblyPath, configNames, vaultName, vaultRootPath,
                        logger, succeeded, suppressedByConfig, allComponentPaths);
                }
                catch (Exception ex) { caught = ex; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            if (!thread.Join(timeout))
            {
                thread.Interrupt();
                KillSolidWorks();
                throw new TimeoutException("SolidWorks timed out processing configurations.");
            }

            if (caught != null)
                throw new InvalidOperationException(
                    $"SolidWorks session failed: {caught.Message}", caught);

            return new ConfigProcessResult
            {
                SucceededConfigs       = succeeded,
                SuppressedComponents   = suppressedByConfig,
                AllComponentLocalPaths = allComponentPaths,
            };
        }

        private static object Invoke(object comObj, string method, object[]? args = null,
            ParameterModifier[]? mods = null)
            => comObj.GetType().InvokeMember(method,
                BindingFlags.InvokeMethod, null, comObj, args, mods, null, null)!;

        private static void SetProp(object comObj, string prop, object value)
        {
            try { comObj.GetType().InvokeMember(prop, BindingFlags.SetProperty, null, comObj, new[] { value }); }
            catch { /* property may not exist on this SW version */ }
        }

        private static void ProcessConfigsInternal(
            string assemblyPath,
            string[] configNames,
            string vaultName,
            string vaultRootPath,
            ILogger logger,
            List<string> succeeded,
            Dictionary<string, List<string>> suppressedByConfig,
            List<string> allComponentPaths)
        {
            var swType = Type.GetTypeFromProgID(SwProgId);
            if (swType == null)
                throw new InvalidOperationException(
                    $"SolidWorks 2023 ({SwProgId}) is not registered on this machine.");

            bool wasRunning = Process.GetProcessesByName("SLDWORKS").Length > 0;
            logger.LogInformation("SolidWorks: connecting (wasRunning={WasRunning})", wasRunning);

            object swApp = wasRunning
                ? ComRot.GetActiveObject("SldWorks.Application")
                : Activator.CreateInstance(swType)!;

            if (!wasRunning)
                Thread.Sleep(TimeSpan.FromSeconds(5));

            SetProp(swApp, "UserControlBackground", true);
            SetProp(swApp, "Visible", false);

            try
            {
                // ── Peek phase: open in the first requested config so GetComponents(true) returns
                // all unsuppressed parts for that config — catching components that PDM indexed
                // under a different (partial) configuration. OpenDoc7 opens in the target config
                // before files are resolved, giving us the full component list earliest.
                object? peekDoc = null;
                try
                {
                    var spec = Invoke(swApp, "GetOpenDocSpec", new object[] { assemblyPath });
                    SetProp(spec, "DocumentType", SwDocAssembly);
                    SetProp(spec, "Silent", true);
                    SetProp(spec, "ConfigurationName", configNames[0]);
                    peekDoc = Invoke(swApp, "OpenDoc7", new object[] { spec });
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "SolidWorks: OpenDoc7 peek open failed");
                }
                logger.LogInformation("SolidWorks: peek open (config='{Config}') returned {Result}",
                    configNames[0], peekDoc == null ? "null" : "doc");

                if (peekDoc != null)
                {
                    try
                    {
                        SyncMissingComponents(peekDoc, configNames, vaultName, vaultRootPath,
                            logger, allComponentPaths);
                        logger.LogInformation(
                            "SolidWorks: pre-sync collected {Count} component path(s): {Paths}",
                            allComponentPaths.Count,
                            string.Join(", ", allComponentPaths.ConvertAll(Path.GetFileName)));
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "SolidWorks: component pre-sync failed (will continue)");
                    }
                    finally
                    {
                        try { Invoke(swApp, "CloseDoc", new object[] { assemblyPath }); } catch { }
                    }
                }
                else
                {
                    logger.LogWarning(
                        "SolidWorks: peek open returned null — pre-sync skipped, zip may be incomplete");
                }

                // ── Main phase: open again (all referenced files now on disk) ─────────────
                var openArgs = new object[] { assemblyPath, SwDocAssembly, SwOpenOptions, "", 0, 0 };
                var openMods = new ParameterModifier(6);
                openMods[4] = true;
                openMods[5] = true;
                object doc = Invoke(swApp, "OpenDoc6", openArgs, new[] { openMods });

                if (doc == null)
                    throw new InvalidOperationException(
                        $"OpenDoc6 returned null for '{assemblyPath}' (SW errors={openArgs[4]}).");

                try
                {
                    foreach (var configName in configNames)
                    {
                        try
                        {
                            bool ok = (bool)Invoke(doc, "ShowConfiguration2", new object[] { configName });
                            if (!ok)
                            {
                                try
                                {
                                    var names = (object[])Invoke(doc, "GetConfigurationNames");
                                    logger.LogWarning(
                                        "ShowConfiguration2 failed for '{Config}'. Available: {All}",
                                        configName, string.Join(", ", (IEnumerable<object>)names));
                                }
                                catch { }
                                continue;
                            }

                            string canonicalName = configName;
                            try
                            {
                                object cfgMgr    = doc.GetType().InvokeMember("ConfigurationManager",
                                    BindingFlags.GetProperty, null, doc, null)!;
                                object activeCfg = cfgMgr.GetType().InvokeMember("ActiveConfiguration",
                                    BindingFlags.GetProperty, null, cfgMgr, null)!;
                                canonicalName    = (string)activeCfg.GetType().InvokeMember("Name",
                                    BindingFlags.GetProperty, null, activeCfg, null)!;
                                logger.LogInformation(
                                    "SolidWorks: active config confirmed as '{Active}' (requested '{Config}')",
                                    canonicalName, configName);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "SolidWorks: could not read active config name");
                            }

                            var suppressedNames = new List<string>();
                            try
                            {
                                var comps = (object[])Invoke(doc, "GetComponents", new object[] { true });
                                if (comps != null)
                                {
                                    foreach (var comp in comps)
                                    {
                                        bool isSuppressed = (bool)Invoke(comp, "IsSuppressed");
                                        if (!isSuppressed) continue;
                                        try
                                        {
                                            var pathName = (string)Invoke(comp, "GetPathName");
                                            if (!string.IsNullOrEmpty(pathName))
                                                suppressedNames.Add(
                                                    Path.GetFileNameWithoutExtension(pathName));
                                        }
                                        catch { }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex,
                                    "Could not enumerate suppressed components for '{Config}'", canonicalName);
                            }
                            suppressedByConfig[canonicalName] = suppressedNames;
                            logger.LogInformation(
                                "Config '{Config}' has {Count} suppressed component(s): {Names}",
                                canonicalName, suppressedNames.Count, string.Join(", ", suppressedNames));

                            Invoke(doc, "Save2", new object[] { true });
                            logger.LogInformation("SolidWorks: saved with config '{Config}' active",
                                canonicalName);

                            succeeded.Add(canonicalName);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex,
                                "Config '{Config}' failed inside SW session — skipping", configName);
                        }
                    }

                    // Re-save in the fullest config (fewest suppressed parts) so APS translates
                    // with maximum geometry — other configs use viewer-side hide/show.
                    if (suppressedByConfig.Count > 0)
                    {
                        var fullestConfig = suppressedByConfig
                            .OrderBy(kv => kv.Value.Count)
                            .First().Key;
                        if (!string.Equals(fullestConfig, succeeded.LastOrDefault(),
                                StringComparison.Ordinal))
                        {
                            try
                            {
                                Invoke(doc, "ShowConfiguration2", new object[] { fullestConfig });
                                Invoke(doc, "Save2", new object[] { true });
                                logger.LogInformation(
                                    "SolidWorks: final save in '{Config}' ({Count} suppressed) for APS",
                                    fullestConfig, suppressedByConfig[fullestConfig].Count);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex,
                                    "SolidWorks: could not re-save fullest config '{Config}'", fullestConfig);
                            }
                        }
                    }
                }
                finally
                {
                    try { Invoke(swApp, "CloseDoc", new object[] { assemblyPath }); } catch { }
                }
            }
            finally
            {
                if (!wasRunning)
                {
                    try { Invoke(swApp, "ExitApp"); } catch { }
                    KillSolidWorks();
                }
            }
        }

        // Activates every config in turn and collects component paths via GetComponents(true),
        // then syncs any files missing from the vault cache. Also walks each sub-assembly's own
        // PDM reference tree to discover nested parts that the root tree missed.
        private static void SyncMissingComponents(
            object doc,
            string[] configNames,
            string vaultName,
            string vaultRootPath,
            ILogger logger,
            List<string> allComponentPaths)
        {
            var compPathSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            GatherComponentPaths(doc, compPathSet, logger, configNames[0]);

            for (int ci = 1; ci < configNames.Length; ci++)
            {
                int before = compPathSet.Count;
                try
                {
                    Invoke(doc, "ShowConfiguration2", new object[] { configNames[ci] });
                    GatherComponentPaths(doc, compPathSet, logger, configNames[ci]);
                    logger.LogInformation(
                        "SolidWorks: config '{Config}' added {Added} new path(s) ({Total} total)",
                        configNames[ci], compPathSet.Count - before, compPathSet.Count);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "SolidWorks: pre-sync ShowConfiguration2 failed for '{Config}'", configNames[ci]);
                }
            }

            var compPaths = compPathSet.ToArray();
            logger.LogInformation(
                "SolidWorks: {Count} distinct component path(s) across {Configs} config(s)",
                compPaths.Length, configNames.Length);
            if (compPaths.Length == 0) return;

            var vaultRoot = Path.GetFullPath(vaultRootPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            EdmVault5? vault = null;
            var pdmVisited           = new HashSet<int>();
            var subAssembliesToWalk  = new List<(IEdmFile5 file, IEdmFolder5 folder)>();
            int synced = 0;

            foreach (var compPath in compPaths)
            {
                var ext = Path.GetExtension(compPath).ToLowerInvariant();
                if (ext != ".sldprt" && ext != ".sldasm" && ext != ".slddrw") continue;

                var fullCompPath = Path.GetFullPath(compPath);
                if (!fullCompPath.StartsWith(vaultRoot, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Component outside vault root, skipping: {Path}", compPath);
                    continue;
                }

                try
                {
                    if (vault == null)
                    {
                        vault = new EdmVault5();
                        vault.LoginAuto(vaultName, 0);
                    }

                    var file = vault.GetFileFromPath(fullCompPath, out IEdmFolder5 folder);
                    if (file == null)
                    {
                        logger.LogWarning("Component not found in vault: {Path}", fullCompPath);
                        continue;
                    }

                    if (!File.Exists(compPath))
                    {
                        object versionObj = (object)0;
                        object? pathObj   = null;
                        file.GetFileCopy(0, ref versionObj, ref pathObj, 0, "");
                        synced++;
                        logger.LogInformation("Pre-sync: fetched '{Name}' from PDM", file.Name);
                    }

                    if (File.Exists(compPath))
                    {
                        allComponentPaths.Add(compPath);
                        if (ext == ".sldasm" && pdmVisited.Add(file.ID))
                            subAssembliesToWalk.Add((file, folder));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Pre-sync: could not process '{Path}'", compPath);
                }
            }

            if (vault != null)
            {
                foreach (var (subFile, subFolder) in subAssembliesToWalk)
                {
                    try
                    {
                        var refTree = subFile.GetReferenceTree(subFolder.ID, 0);
                        if (refTree != null)
                            WalkAndSyncPdmTree(refTree, isRoot: true, vault, vaultRoot,
                                pdmVisited, allComponentPaths, ref synced, logger);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Pre-sync: PDM tree walk failed for sub-assembly '{Name}'", subFile.Name);
                    }
                }
            }

            logger.LogInformation(
                "SolidWorks pre-sync: {Total} path(s) collected, {Synced} newly synced from PDM",
                allComponentPaths.Count, synced);
        }

        private static void GatherComponentPaths(object doc, HashSet<string> pathSet,
            ILogger logger, string configLabel)
        {
            object[] comps;
            try { comps = (object[])Invoke(doc, "GetComponents", new object[] { true }); }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "SolidWorks: GetComponents failed for config '{Config}'", configLabel);
                return;
            }
            if (comps == null) return;
            foreach (var comp in comps)
                try
                {
                    var path = (string)Invoke(comp, "GetPathName");
                    if (!string.IsNullOrEmpty(path)) pathSet.Add(path);
                }
                catch { }
            logger.LogInformation(
                "SolidWorks: config '{Config}': {Count} component(s) from GetComponents",
                configLabel, comps.Length);
        }

        private static void WalkAndSyncPdmTree(
            IEdmReference5 node, bool isRoot,
            EdmVault5 vault, string vaultRoot,
            HashSet<int> visited, List<string> allComponentPaths,
            ref int synced, ILogger logger)
        {
            if (node == null) return;

            var nodeFile   = node.File;
            var nodeFolder = node.Folder;

            if (nodeFile != null && nodeFolder != null && visited.Add(nodeFile.ID))
            {
                var ext = Path.GetExtension(nodeFile.Name).ToLowerInvariant();
                if (ext == ".sldprt" || ext == ".sldasm" || ext == ".slddrw")
                {
                    var localPath = nodeFile.GetLocalPath(nodeFolder.ID);
                    if (!string.IsNullOrEmpty(localPath) && !File.Exists(localPath))
                    {
                        try
                        {
                            object v  = (object)0;
                            object? p = null;
                            nodeFile.GetFileCopy(0, ref v, ref p, 0, "");
                            synced++;
                            logger.LogInformation("Pre-sync: fetched nested '{Name}' from PDM",
                                nodeFile.Name);
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Pre-sync: could not sync nested '{Name}'",
                                nodeFile.Name);
                        }
                    }

                    localPath = nodeFile.GetLocalPath(nodeFolder.ID);
                    if (!string.IsNullOrEmpty(localPath) && File.Exists(localPath))
                        allComponentPaths.Add(localPath);
                }
            }

            string projName = "";
            IEdmPos5 pos;
            try { pos = node.GetFirstChildPosition(ref projName, isRoot, true, 0); }
            catch { return; }

            while (!pos.IsNull)
            {
                IEdmReference5 child;
                try { child = node.GetNextChild(pos); }
                catch { break; }

                if (child != null)
                    WalkAndSyncPdmTree(child, false, vault, vaultRoot,
                        visited, allComponentPaths, ref synced, logger);
            }
        }

        private static void KillSolidWorks()
        {
            foreach (var p in Process.GetProcessesByName("SLDWORKS"))
                try { p.Kill(); } catch { }
        }
    }
}
