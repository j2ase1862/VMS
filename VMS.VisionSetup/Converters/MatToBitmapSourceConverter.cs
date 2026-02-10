using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace VMS.VisionSetup.Converters
{
    public class MatToBitmapSourceConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Mat mat && !mat.Empty())
                return mat.ToBitmapSource();
            return null;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
