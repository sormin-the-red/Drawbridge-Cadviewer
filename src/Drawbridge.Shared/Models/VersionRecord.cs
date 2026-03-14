using System.Collections.Generic;

namespace Drawbridge.Shared.Models
{
    public class VersionRecord
    {
        public string PartNumber { get; set; }
        public int Version { get; set; }
        public string Status { get; set; }
        public string ApsUrn { get; set; }
        public Dictionary<string, string> ConfigViewableGuids { get; set; }
        public Dictionary<string, string> ConfigUrns { get; set; }
        public List<string> Configurations { get; set; }
        public Dictionary<string, List<string>> ConfigSuppressedComponents { get; set; }
        public bool HasDrawing { get; set; }
        public string DrawingUrl { get; set; }
        public string ThumbnailUrl { get; set; }
        public string SubmittedBy { get; set; }
        public string ConvertedAt { get; set; }
        public string OwnerName { get; set; }
        public string OwnerEmail { get; set; }
        public List<string> MarkupOrder { get; set; }
        public string ErrorMessage { get; set; }
    }
}
