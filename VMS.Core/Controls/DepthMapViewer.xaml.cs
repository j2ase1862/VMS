using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VMS.Camera.Models;

namespace VMS.Core.Controls
{
    public partial class DepthMapViewer : UserControl
    {
        private Point _panStart;
        private double _translateStartX;
        private double _translateStartY;
        private bool _isPanning;

        #region Dependency Properties

        public static readonly DependencyProperty PointCloudProperty =
            DependencyProperty.Register(
                nameof(PointCloud),
                typeof(PointCloudData),
                typeof(DepthMapViewer),
                new PropertyMetadata(null, OnPointCloudChanged));

        public PointCloudData? PointCloud
        {
            get => (PointCloudData?)GetValue(PointCloudProperty);
            set => SetValue(PointCloudProperty, value);
        }

        #endregion

        public DepthMapViewer()
        {
            InitializeComponent();

            DepthImage.MouseWheel += OnMouseWheel;
            DepthImage.MouseLeftButtonDown += OnMouseLeftButtonDown;
            DepthImage.MouseLeftButtonUp += OnMouseLeftButtonUp;
            DepthImage.MouseMove += OnMouseMove;
            DepthImage.MouseLeftButtonDown += OnMouseDoubleClick;
        }

        #region Rendering

        private static void OnPointCloudChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DepthMapViewer viewer)
                viewer.RenderDepthMap();
        }

        private void RenderDepthMap()
        {
            var data = PointCloud;
            if (data == null || data.PointCount == 0)
            {
                ShowPlaceholder("No Depth Data");
                return;
            }

            if (!data.IsOrganized)
            {
                ShowPlaceholder("Requires organized point cloud");
                return;
            }

            int width = data.GridWidth;
            int height = data.GridHeight;
            var positions = data.Positions;
            int pointCount = data.PointCount;

            // Calculate Z range — depth/height axis (exclude invalid points)
            float zMin = float.MaxValue;
            float zMax = float.MinValue;

            for (int i = 0; i < pointCount; i++)
            {
                var p = positions[i];
                // Skip invalid points: (0,0,0) or NaN/Infinity
                if (p.X == 0f && p.Y == 0f && p.Z == 0f) continue;
                if (float.IsNaN(p.Z) || float.IsInfinity(p.Z)) continue;

                if (p.Z < zMin) zMin = p.Z;
                if (p.Z > zMax) zMax = p.Z;
            }

            if (zMin >= zMax)
            {
                zMin = 0f;
                zMax = 1f;
            }

            float range = zMax - zMin;

            // Create WriteableBitmap (Bgr24)
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr24, null);
            int stride = width * 3;
            byte[] pixels = new byte[stride * height];

            for (int row = 0; row < height; row++)
            {
                int rowOffset = row * stride;
                for (int col = 0; col < width; col++)
                {
                    int idx = row * width + col;
                    int pixelOffset = rowOffset + col * 3;

                    if (idx >= pointCount)
                    {
                        // Out of bounds — black
                        pixels[pixelOffset] = 0;
                        pixels[pixelOffset + 1] = 0;
                        pixels[pixelOffset + 2] = 0;
                        continue;
                    }

                    var p = positions[idx];

                    // Invalid point → black
                    if ((p.X == 0f && p.Y == 0f && p.Z == 0f) ||
                        float.IsNaN(p.Z) || float.IsInfinity(p.Z))
                    {
                        pixels[pixelOffset] = 0;
                        pixels[pixelOffset + 1] = 0;
                        pixels[pixelOffset + 2] = 0;
                        continue;
                    }

                    float t = (p.Z - zMin) / range;
                    JetColormapBgr(t, out byte b, out byte g, out byte r);
                    pixels[pixelOffset] = b;
                    pixels[pixelOffset + 1] = g;
                    pixels[pixelOffset + 2] = r;
                }
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);
            bitmap.Freeze();

            DepthImage.Source = bitmap;
            DepthImage.Visibility = Visibility.Visible;
            PlaceholderPanel.Visibility = Visibility.Collapsed;

            // Update color bar labels
            MaxValueLabel.Text = zMax.ToString("F1");
            MinValueLabel.Text = zMin.ToString("F1");

            // Reset zoom/pan on new data
            ResetTransform();
        }

        private void ShowPlaceholder(string message)
        {
            DepthImage.Source = null;
            DepthImage.Visibility = Visibility.Collapsed;
            PlaceholderText.Text = message;
            PlaceholderPanel.Visibility = Visibility.Visible;
            MaxValueLabel.Text = "--";
            MinValueLabel.Text = "--";
        }

        /// <summary>
        /// Jet colormap: maps [0,1] → BGR bytes. Blue→Cyan→Green→Yellow→Red.
        /// </summary>
        private static void JetColormapBgr(float t, out byte b, out byte g, out byte r)
        {
            t = Math.Clamp(t, 0f, 1f);

            float rf, gf, bf;

            if (t < 0.25f)
            {
                float s = t / 0.25f;
                rf = 0f; gf = s; bf = 1f;
            }
            else if (t < 0.5f)
            {
                float s = (t - 0.25f) / 0.25f;
                rf = 0f; gf = 1f; bf = 1f - s;
            }
            else if (t < 0.75f)
            {
                float s = (t - 0.5f) / 0.25f;
                rf = s; gf = 1f; bf = 0f;
            }
            else
            {
                float s = (t - 0.75f) / 0.25f;
                rf = 1f; gf = 1f - s; bf = 0f;
            }

            r = (byte)(rf * 255f);
            g = (byte)(gf * 255f);
            b = (byte)(bf * 255f);
        }

        #endregion

        #region Mouse: Wheel=Zoom, Left Drag=Pan, Double-click=Reset

        private void OnMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var pos = e.GetPosition(DepthImage);
            double zoom = e.Delta > 0 ? 1.2 : 1.0 / 1.2;

            double oldScale = ImageScale.ScaleX;
            double newScale = oldScale * zoom;
            newScale = Math.Clamp(newScale, 0.1, 50.0);

            // Zoom toward cursor position
            double relX = pos.X;
            double relY = pos.Y;

            ImageTranslate.X = relX - (relX - ImageTranslate.X) * (newScale / oldScale);
            ImageTranslate.Y = relY - (relY - ImageTranslate.Y) * (newScale / oldScale);

            ImageScale.ScaleX = newScale;
            ImageScale.ScaleY = newScale;

            e.Handled = true;
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                return; // handled by double-click

            _panStart = e.GetPosition(this);
            _translateStartX = ImageTranslate.X;
            _translateStartY = ImageTranslate.Y;
            _isPanning = true;
            DepthImage.CaptureMouse();
            e.Handled = true;
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                DepthImage.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isPanning) return;

            var current = e.GetPosition(this);
            ImageTranslate.X = _translateStartX + (current.X - _panStart.X);
            ImageTranslate.Y = _translateStartY + (current.Y - _panStart.Y);
            e.Handled = true;
        }

        private void OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ResetTransform();
                e.Handled = true;
            }
        }

        private void ResetTransform()
        {
            ImageScale.ScaleX = 1.0;
            ImageScale.ScaleY = 1.0;
            ImageTranslate.X = 0;
            ImageTranslate.Y = 0;
        }

        #endregion
    }
}
