using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VMS.Camera.Models;
using VMS.Core.Controls;

namespace VMS.VisionSetup.Controls
{
    public partial class PointDrawingCanvas : UserControl
    {
        private bool _suppressSliderEvent;

        #region Dependency Properties

        public static readonly DependencyProperty PointCloudProperty =
            DependencyProperty.Register(
                nameof(PointCloud),
                typeof(PointCloudData),
                typeof(PointDrawingCanvas),
                new PropertyMetadata(null, OnPointCloudChanged));

        public PointCloudData? PointCloud
        {
            get => (PointCloudData?)GetValue(PointCloudProperty);
            set => SetValue(PointCloudProperty, value);
        }

        public static readonly DependencyProperty HeightSliceBaselineProperty =
            DependencyProperty.Register(
                nameof(HeightSliceBaseline),
                typeof(double),
                typeof(PointDrawingCanvas),
                new PropertyMetadata(double.NaN, OnHeightSlicePropertyChanged));

        public double HeightSliceBaseline
        {
            get => (double)GetValue(HeightSliceBaselineProperty);
            set => SetValue(HeightSliceBaselineProperty, value);
        }

        public static readonly DependencyProperty HeightSliceLowerProperty =
            DependencyProperty.Register(
                nameof(HeightSliceLower),
                typeof(double),
                typeof(PointDrawingCanvas),
                new PropertyMetadata(double.NaN, OnHeightSlicePropertyChanged));

        public double HeightSliceLower
        {
            get => (double)GetValue(HeightSliceLowerProperty);
            set => SetValue(HeightSliceLowerProperty, value);
        }

        public static readonly DependencyProperty HeightSliceUpperProperty =
            DependencyProperty.Register(
                nameof(HeightSliceUpper),
                typeof(double),
                typeof(PointDrawingCanvas),
                new PropertyMetadata(double.NaN, OnHeightSlicePropertyChanged));

        public double HeightSliceUpper
        {
            get => (double)GetValue(HeightSliceUpperProperty);
            set => SetValue(HeightSliceUpperProperty, value);
        }

        #endregion

        public PointDrawingCanvas()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            InnerViewer.PointCloudRendered += OnPointCloudRendered;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            InnerViewer.PointCloudRendered -= OnPointCloudRendered;
        }

        #region Point Cloud Changed → Forward to InnerViewer

        private static void OnPointCloudChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PointDrawingCanvas canvas)
            {
                canvas.InnerViewer.PointCloud = canvas.PointCloud;

                if (canvas.PointCloud == null || canvas.PointCloud.PointCount == 0)
                {
                    canvas.ColorBarPanel.Visibility = Visibility.Collapsed;
                    canvas.InnerViewer.StatusText = "3D View Ready";
                }
            }
        }

        private void OnPointCloudRendered(float yMin, float yMax, int displayCount, int totalCount)
        {
            // Setup sliders to match data range
            _suppressSliderEvent = true;
            SliderMin.Minimum = yMin;
            SliderMin.Maximum = yMax;
            SliderMin.Value = yMin;
            SliderMax.Minimum = yMin;
            SliderMax.Maximum = yMax;
            SliderMax.Value = yMax;
            _suppressSliderEvent = false;

            MinValueLabel.Text = yMin.ToString("F1");
            MaxValueLabel.Text = yMax.ToString("F1");
            ColorBarPanel.Visibility = Visibility.Visible;

            InnerViewer.StatusText = $"Loaded: {PointCloud?.Name} ({totalCount:N0} points)";
        }

        #endregion

        #region Height Slice Preview

        private static void OnHeightSlicePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PointDrawingCanvas canvas)
            {
                canvas.ApplyHeightSlicePreview();
            }
        }

        private void ApplyHeightSlicePreview()
        {
            if (double.IsNaN(HeightSliceBaseline) || double.IsNaN(HeightSliceLower) || double.IsNaN(HeightSliceUpper))
                return;
            if (PointCloud == null || PointCloud.PointCount == 0)
                return;

            var positions = PointCloud.Positions;
            int totalCount = PointCloud.PointCount;
            int stride = InnerViewer.GetCurrentLodStride();
            float baseline = (float)HeightSliceBaseline;
            float lower = (float)HeightSliceLower;
            float upper = (float)HeightSliceUpper;
            float range = upper - lower;

            var colors = new Color4Collection();
            var darkGray = new Color4(0.12f, 0.12f, 0.12f, 1.0f);
            int inCount = 0;

            for (int i = 0; i < totalCount; i += stride)
            {
                float normalizedZ = positions[i].Z - baseline;
                if (normalizedZ >= lower && normalizedZ <= upper)
                {
                    float t = range > 0.0001f ? (normalizedZ - lower) / range : 0.5f;
                    colors.Add(PointCloudViewer.JetColormap(t));
                    inCount++;
                }
                else
                {
                    colors.Add(darkGray);
                }
            }

            InnerViewer.ApplyColors(colors);
            InnerViewer.StatusText = $"Height Slice: {inCount:N0} / {totalCount:N0} points in range";
        }

        #endregion

        #region Slider Events

        private void SliderMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvent || SliderMax == null || MinValueLabel == null) return;

            if (SliderMin.Value > SliderMax.Value)
            {
                _suppressSliderEvent = true;
                SliderMin.Value = SliderMax.Value;
                _suppressSliderEvent = false;
            }

            MinValueLabel.Text = SliderMin.Value.ToString("F1");
            InnerViewer.RecolorWithRange((float)SliderMin.Value, (float)SliderMax.Value);
        }

        private void SliderMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvent || SliderMin == null || MaxValueLabel == null) return;

            if (SliderMax.Value < SliderMin.Value)
            {
                _suppressSliderEvent = true;
                SliderMax.Value = SliderMin.Value;
                _suppressSliderEvent = false;
            }

            MaxValueLabel.Text = SliderMax.Value.ToString("F1");
            InnerViewer.RecolorWithRange((float)SliderMin.Value, (float)SliderMax.Value);
        }

        private void SliderMin_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double step = (SliderMin.Maximum - SliderMin.Minimum) * 0.02;
            SliderMin.Value += e.Delta > 0 ? step : -step;
            e.Handled = true;
        }

        private void SliderMax_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            double step = (SliderMax.Maximum - SliderMax.Minimum) * 0.02;
            SliderMax.Value += e.Delta > 0 ? step : -step;
            e.Handled = true;
        }

        #endregion
    }
}
