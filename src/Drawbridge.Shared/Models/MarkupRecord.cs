namespace Drawbridge.Shared.Models
{
    public class MarkupRecord
    {
        public string MarkupId { get; set; }
        public string PartNumber { get; set; }
        public int Version { get; set; }
        public string VersionKey { get; set; }
        public string PreviewUrl { get; set; }
        public string DataUrl { get; set; }
        public int CanvasWidth { get; set; }
        public int CanvasHeight { get; set; }
        public string CreatedBy { get; set; }
        public string CreatedAt { get; set; }
        public string Title { get; set; }
    }
}
