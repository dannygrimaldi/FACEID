namespace Face.Api.Core.FacePipeline.Models
{
    public class FaceDetection
    {
        public System.Drawing.Rectangle BoundingBox { get; set; }

        public System.Drawing.PointF[] Landmarks { get; set; } 
            = Array.Empty<System.Drawing.PointF>();

        public float Score { get; set; }
    }
}
