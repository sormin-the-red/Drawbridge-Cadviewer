using System.Collections.Generic;

namespace Drawbridge.Shared.Models
{
    public class AnnotationRecord
    {
        public string AnnotationId { get; set; }
        public string PartNumber { get; set; }
        public int Version { get; set; }
        public string VersionKey { get; set; }
        public double WorldX { get; set; }
        public double WorldY { get; set; }
        public double WorldZ { get; set; }
        public List<string> ComponentIds { get; set; }
        public string Text { get; set; }
        public string SubmittedBy { get; set; }
        public string CreatedAt { get; set; }
        public bool Resolved { get; set; }
        public string ViewerState { get; set; }
        public string ParentId { get; set; }
        public string MarkupSvg { get; set; }
        public List<string> Mentions { get; set; }
    }
}
