namespace Drawbridge.Shared.Models
{
    public class JobRecord
    {
        public string JobId { get; set; }
        public string Status { get; set; }
        public string Progress { get; set; }
        public string VaultFilePath { get; set; }
        public int PdmVersion { get; set; }
        public string SubmittedBy { get; set; }
        public string Description { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public string ErrorMessage { get; set; }
    }
}
