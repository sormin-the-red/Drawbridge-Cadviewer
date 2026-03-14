namespace Drawbridge.Shared.Models
{
    public class ProductRecord
    {
        public string PartNumber { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public int LatestVersion { get; set; }
        public string ThumbnailUrl { get; set; }
        public string UpdatedAt { get; set; }
    }
}
