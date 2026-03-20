namespace Drawbridge.Api.Services
{
    public class SubmitJobRequest
    {
        public string? VaultName      { get; set; }
        public string? VaultFilePath  { get; set; }
        public int     PdmVersion     { get; set; }
        public string[]? Configurations  { get; set; }
        public string[]? FbxVaultPaths   { get; set; }
        public string[]? StlVaultPaths   { get; set; }
        public string[]? SkpVaultPaths   { get; set; }
        public string? SubmittedBy    { get; set; }
        public string? Description    { get; set; }
        public string? OwnerName      { get; set; }
        public string? OwnerEmail     { get; set; }
    }
}
