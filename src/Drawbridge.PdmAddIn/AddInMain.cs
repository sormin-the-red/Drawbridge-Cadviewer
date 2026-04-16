using EPDM.Interop.epdm;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

namespace Drawbridge.PdmAddIn
{
    [ComVisible(true)]
    [Guid("A3F5D8E2-1C4B-4F72-8A6E-3D9B2C7E1F48")]
    public class AddInMain : IEdmAddIn5
    {
        private const int CmdPublish = 1;

        public void GetAddInInfo(ref EdmAddInInfo poInfo, IEdmVault5 poVault, IEdmCmdMgr5 poCmdMgr)
        {
            poInfo.mbsAddInName    = "Drawbridge Publisher";
            poInfo.mbsCompany      = "Creative Works";
            poInfo.mbsDescription  = "Publish a SolidWorks part or assembly to the Drawbridge web viewer";
            poInfo.mlAddInVersion  = 1;
            poInfo.mlRequiredVersionMajor = 29;

            poCmdMgr.AddCmd(CmdPublish, "Publish to Drawbridge",
                (int)EdmMenuFlags.EdmMenu_Nothing, "", null, 0);
        }

        public void OnCmd(ref EdmCmd poCmd, ref EdmCmdData[] ppoData)
        {
            if (poCmd.mlCmdID != CmdPublish) return;
            if (ppoData == null || ppoData.Length == 0) return;

            var data  = ppoData[0];
            var vault = (IEdmVault5)poCmd.mpoVault;

            try
            {
                var file = vault.GetFileFromPath(data.mbsStrData2, out IEdmFolder5 folder);
                if (file == null)
                {
                    MessageBox.Show("Could not locate the selected file in the vault.",
                        "Drawbridge", MessageBoxButtons.OK, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                    return;
                }

                var ext = Path.GetExtension(data.mbsStrData2).ToLower();
                if (ext != ".sldasm" && ext != ".sldprt")
                {
                    MessageBox.Show("Only .sldasm and .sldprt files can be published.",
                        "Drawbridge", MessageBoxButtons.OK, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                    return;
                }

                bool   isAssembly     = ext == ".sldasm";
                int    currentVersion = file.CurrentVersion;
                string fileName       = Path.GetFileName(data.mbsStrData2);

                var configurations = isAssembly
                    ? GetFileConfigurations(file)
                    : Array.Empty<string>();

                var fbxVaultPaths = GetFilesByExtension(folder, data.mbsStrData2, ".fbx");
                var stlVaultPaths = GetFilesByExtension(folder, data.mbsStrData2, ".stl");
                var skpVaultPaths = GetFilesByExtension(folder, data.mbsStrData2, ".skp");
                var description   = GetFileDescription(file);

                using (var dialog = new PublishDialog(
                    fileName, currentVersion, isAssembly,
                    configurations, fbxVaultPaths, stlVaultPaths, skpVaultPaths))
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return;

                    SubmitJob(
                        vault.Name,
                        data.mbsStrData2,
                        currentVersion,
                        isAssembly ? dialog.SelectedConfigurations : Array.Empty<string>(),
                        dialog.SelectedFbxPaths,
                        dialog.SelectedStlPaths,
                        dialog.SelectedSkpPaths,
                        Environment.UserName,
                        description,
                        dialog.SelectedOwnerName,
                        dialog.SelectedOwnerEmail);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Drawbridge error: {ex.Message}",
                    "Drawbridge", MessageBoxButtons.OK, MessageBoxIcon.Error,
                    MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
            }
        }

        // ── PDM helpers ───────────────────────────────────────────────────────────────

        private static string[] GetFileConfigurations(IEdmFile5 file)
        {
            var configs = new List<string>();
            try
            {
                object versionNo = file.CurrentVersion;
                var strList      = file.GetConfigurations(ref versionNo);
                var pos          = strList.GetHeadPosition();
                while (!pos.IsNull)
                {
                    var name = strList.GetNext(pos);
                    if (!string.IsNullOrEmpty(name) && name != "@")
                        configs.Add(name);
                }
            }
            catch { }
            return configs.ToArray();
        }

        private static string[] GetFilesByExtension(
            IEdmFolder5 folder, string assemblyVaultPath, string ext)
        {
            var result = new List<string>();
            try
            {
                var folderVaultPath = Path.GetDirectoryName(assemblyVaultPath);
                var pos             = folder.GetFirstFilePosition();
                while (!pos.IsNull)
                {
                    var f = folder.GetNextFile(pos);
                    if (f != null &&
                        Path.GetExtension(f.Name).Equals(ext, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(folderVaultPath + "\\" + f.Name);
                    }
                }
            }
            catch { }
            return result.ToArray();
        }

        private static void SubmitJob(
            string vaultName, string vaultFilePath, int pdmVersion,
            string[] configurations, string[] fbxVaultPaths, string[] stlVaultPaths,
            string[] skpVaultPaths, string submittedBy, string description,
            string ownerName, string ownerEmail)
        {
            var json = BuildJson(vaultName, vaultFilePath, pdmVersion, configurations,
                fbxVaultPaths, stlVaultPaths, skpVaultPaths, submittedBy,
                DateTime.UtcNow.ToString("o"), description, ownerName, ownerEmail);

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("x-api-key", ApiConfig.ApiKey);
                var content  = new StringContent(json, Encoding.UTF8, "application/json");
                var response = client.PostAsync(ApiConfig.JobsEndpoint, content)
                                     .GetAwaiter().GetResult();

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show(
                        "Queued for conversion. The model will appear in the viewer once processing completes.",
                        "Drawbridge", MessageBoxButtons.OK, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                }
                else
                {
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    MessageBox.Show(
                        $"Failed to queue job (HTTP {(int)response.StatusCode}):\n{body}",
                        "Drawbridge", MessageBoxButtons.OK, MessageBoxIcon.Error,
                        MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                }
            }
        }

        private static string BuildJson(
            string vaultName, string vaultFilePath, int pdmVersion,
            string[] configurations, string[] fbxVaultPaths, string[] stlVaultPaths,
            string[] skpVaultPaths, string submittedBy, string submittedAt,
            string description, string ownerName, string ownerEmail)
        {
            var configsJson = "[" + string.Join(",",
                (configurations  ?? new string[0]).Select(c => "\"" + EscapeJson(c) + "\"")) + "]";
            var fbxJson     = "[" + string.Join(",",
                (fbxVaultPaths   ?? new string[0]).Select(p => "\"" + EscapeJson(p) + "\"")) + "]";
            var stlJson     = "[" + string.Join(",",
                (stlVaultPaths   ?? new string[0]).Select(p => "\"" + EscapeJson(p) + "\"")) + "]";
            var skpJson     = "[" + string.Join(",",
                (skpVaultPaths   ?? new string[0]).Select(p => "\"" + EscapeJson(p) + "\"")) + "]";

            return "{"
                + "\"vaultName\":\""     + EscapeJson(vaultName)    + "\","
                + "\"vaultFilePath\":\"" + EscapeJson(vaultFilePath) + "\","
                + "\"pdmVersion\":"      + pdmVersion                + ","
                + "\"configurations\":"  + configsJson               + ","
                + "\"fbxVaultPaths\":"   + fbxJson                   + ","
                + "\"stlVaultPaths\":"   + stlJson                   + ","
                + "\"skpVaultPaths\":"   + skpJson                   + ","
                + "\"submittedBy\":\""   + EscapeJson(submittedBy)   + "\","
                + "\"submittedAt\":\""   + EscapeJson(submittedAt)   + "\","
                + "\"description\":\""   + EscapeJson(description)   + "\","
                + "\"ownerName\":\""     + EscapeJson(ownerName)     + "\","
                + "\"ownerEmail\":\""    + EscapeJson(ownerEmail)    + "\""
                + "}";
        }

        private static string GetFileDescription(IEdmFile5 file)
        {
            try
            {
                var enumVar = (IEdmEnumeratorVariable8)file.GetEnumeratorVariable();
                if (enumVar.GetVar("Description", "@", out object varValue) && varValue != null)
                    return varValue.ToString() ?? "";
            }
            catch { }
            return "";
        }

        private static string EscapeJson(string? s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
