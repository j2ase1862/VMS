using System.Windows.Media.Imaging;

namespace VMS.Models
{
    public class NgImageItem
    {
        public BitmapSource? Thumbnail { get; set; }
        public string CameraName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
