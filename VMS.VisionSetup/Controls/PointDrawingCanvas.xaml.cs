using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Controls
{
    public partial class PointDrawingCanvas : UserControl
    {
        private DefaultEffectsManager? _effectsManager;
        private float _dataYMin;
        private float _dataYMax;
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

        public static readonly DependencyProperty PointSizeProperty =
            DependencyProperty.Register(
                nameof(PointSize),
                typeof(double),
                typeof(PointDrawingCanvas),
                new PropertyMetadata(3.0));

        public double PointSize
        {
            get => (double)GetValue(PointSizeProperty);
            set => SetValue(PointSizeProperty, value);
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
            if (_effectsManager == null)
            {
                _effectsManager = new DefaultEffectsManager();
                Viewport.EffectsManager = _effectsManager;
            }
            BuildGridLines();
            BuildAxisLines();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_effectsManager != null)
            {
                Viewport.EffectsManager = null;
                _effectsManager.Dispose();
                _effectsManager = null;
            }
        }

        #region Grid and Axis

        private void BuildGridLines()
        {
            var builder = new LineBuilder();
            float extent = 500f;
            float step = 50f;

            for (float i = -extent; i <= extent; i += step)
            {
                builder.AddLine(new Vector3(i, 0, -extent), new Vector3(i, 0, extent));
                builder.AddLine(new Vector3(-extent, 0, i), new Vector3(extent, 0, i));
            }

            GridLines.Geometry = builder.ToLineGeometry3D();
        }

        private void BuildAxisLines()
        {
            var builder = new LineBuilder();
            float len = 200f;

            // X axis - Red
            builder.AddLine(Vector3.Zero, new Vector3(len, 0, 0));
            // Y axis - Green
            builder.AddLine(Vector3.Zero, new Vector3(0, len, 0));
            // Z axis - Blue
            builder.AddLine(Vector3.Zero, new Vector3(0, 0, len));

            var geo = builder.ToLineGeometry3D();

            // Color per segment (2 vertices per line)
            var axisColors = new Color4Collection
            {
                new Color4(1, 0, 0, 1), new Color4(1, 0, 0, 1),   // Red - X
                new Color4(0, 1, 0, 1), new Color4(0, 1, 0, 1),   // Green - Y
                new Color4(0, 0, 1, 1), new Color4(0, 0, 1, 1)    // Blue - Z
            };
            geo.Colors = axisColors;

            AxisLines.Geometry = geo;
        }

        #endregion

        #region Point Cloud Rendering

        private static void OnPointCloudChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PointDrawingCanvas canvas)
            {
                canvas.UpdatePointCloud();
            }
        }

        private void UpdatePointCloud()
        {
            var data = PointCloud;
            if (data == null || data.Positions.Length == 0)
            {
                PointCloudModel.Geometry = null;
                PointCountText.Text = "No points";
                StatusText.Text = "3D View Ready";
                ColorBarPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var positions = data.Positions;

            // Compute Y min/max from data
            _dataYMin = float.MaxValue;
            _dataYMax = float.MinValue;
            foreach (var p in positions)
            {
                if (p.Y < _dataYMin) _dataYMin = p.Y;
                if (p.Y > _dataYMax) _dataYMax = p.Y;
            }

            // Setup sliders
            _suppressSliderEvent = true;
            SliderMin.Minimum = _dataYMin;
            SliderMin.Maximum = _dataYMax;
            SliderMin.Value = _dataYMin;
            SliderMax.Minimum = _dataYMin;
            SliderMax.Maximum = _dataYMax;
            SliderMax.Value = _dataYMax;
            _suppressSliderEvent = false;

            MinValueLabel.Text = _dataYMin.ToString("F1");
            MaxValueLabel.Text = _dataYMax.ToString("F1");

            // Build geometry with positions and indices
            var pointPositions = new Vector3Collection(positions.Length);
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                pointPositions.Add(new Vector3(p.X, p.Y, p.Z));
            }

            var indices = new IntCollection(positions.Length);
            for (int i = 0; i < positions.Length; i++)
                indices.Add(i);

            var pointColors = BuildJetColors(positions, _dataYMin, _dataYMax);

            var geometry = new PointGeometry3D
            {
                Positions = pointPositions,
                Colors = pointColors,
                Indices = indices
            };

            PointCloudModel.Geometry = geometry;

            ColorBarPanel.Visibility = Visibility.Visible;
            PointCountText.Text = $"{positions.Length:N0} points";
            StatusText.Text = $"Loaded: {data.Name} ({positions.Length:N0} points)";

            AutoFitCamera(positions);
        }

        #endregion

        #region Jet Colormap

        private static Color4 JetColormap(float t)
        {
            // Clamp to [0,1]
            t = Math.Clamp(t, 0f, 1f);

            float r, g, b;

            if (t < 0.25f)
            {
                // Blue → Cyan: R=0, G rises, B=1
                float s = t / 0.25f;
                r = 0f; g = s; b = 1f;
            }
            else if (t < 0.5f)
            {
                // Cyan → Green: R=0, G=1, B falls
                float s = (t - 0.25f) / 0.25f;
                r = 0f; g = 1f; b = 1f - s;
            }
            else if (t < 0.75f)
            {
                // Green → Yellow: R rises, G=1, B=0
                float s = (t - 0.5f) / 0.25f;
                r = s; g = 1f; b = 0f;
            }
            else
            {
                // Yellow → Red: R=1, G falls, B=0
                float s = (t - 0.75f) / 0.25f;
                r = 1f; g = 1f - s; b = 0f;
            }

            return new Color4(r, g, b, 1f);
        }

        private static Color4Collection BuildJetColors(Vector3[] positions, float yMin, float yMax)
        {
            var colors = new Color4Collection(positions.Length);
            float range = yMax - yMin;

            for (int i = 0; i < positions.Length; i++)
            {
                float t = range > 0.0001f ? (positions[i].Y - yMin) / range : 0.5f;
                colors.Add(JetColormap(t));
            }

            return colors;
        }

        private void RecolorPoints()
        {
            if (PointCloud == null || PointCloud.Positions.Length == 0) return;
            if (PointCloudModel.Geometry is not PointGeometry3D) return;

            float sliderMin = (float)SliderMin.Value;
            float sliderMax = (float)SliderMax.Value;

            var positions = PointCloud.Positions;
            var colors = BuildJetColors(positions, sliderMin, sliderMax);

            // Fully rebuild geometry from source data to force GPU buffer update
            var pointPositions = new Vector3Collection(positions.Length);
            var indices = new IntCollection(positions.Length);
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                pointPositions.Add(new Vector3(p.X, p.Y, p.Z));
                indices.Add(i);
            }

            PointCloudModel.Geometry = null;
            PointCloudModel.Geometry = new PointGeometry3D
            {
                Positions = pointPositions,
                Indices = indices,
                Colors = colors
            };
        }

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
            if (PointCloud == null || PointCloud.Positions.Length == 0)
                return;
            if (PointCloudModel.Geometry is not PointGeometry3D)
                return;

            var positions = PointCloud.Positions;
            float baseline = (float)HeightSliceBaseline;
            float lower = (float)HeightSliceLower;
            float upper = (float)HeightSliceUpper;
            float range = upper - lower;

            var colors = new Color4Collection(positions.Length);
            var darkGray = new Color4(0.12f, 0.12f, 0.12f, 1.0f);
            int inCount = 0;

            for (int i = 0; i < positions.Length; i++)
            {
                float normalizedY = positions[i].Y - baseline;
                if (normalizedY >= lower && normalizedY <= upper)
                {
                    float t = range > 0.0001f ? (normalizedY - lower) / range : 0.5f;
                    colors.Add(JetColormap(t));
                    inCount++;
                }
                else
                {
                    colors.Add(darkGray);
                }
            }

            var pointPositions = new Vector3Collection(positions.Length);
            var indices = new IntCollection(positions.Length);
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                pointPositions.Add(new Vector3(p.X, p.Y, p.Z));
                indices.Add(i);
            }

            PointCloudModel.Geometry = null;
            PointCloudModel.Geometry = new PointGeometry3D
            {
                Positions = pointPositions,
                Indices = indices,
                Colors = colors
            };

            StatusText.Text = $"Height Slice: {inCount:N0} / {positions.Length:N0} points in range";
        }

        private void SliderMin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvent || SliderMax == null || MinValueLabel == null) return;

            // Clamp min to not exceed max
            if (SliderMin.Value > SliderMax.Value)
            {
                _suppressSliderEvent = true;
                SliderMin.Value = SliderMax.Value;
                _suppressSliderEvent = false;
            }

            MinValueLabel.Text = SliderMin.Value.ToString("F1");
            RecolorPoints();
        }

        private void SliderMax_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressSliderEvent || SliderMin == null || MaxValueLabel == null) return;

            // Clamp max to not go below min
            if (SliderMax.Value < SliderMin.Value)
            {
                _suppressSliderEvent = true;
                SliderMax.Value = SliderMin.Value;
                _suppressSliderEvent = false;
            }

            MaxValueLabel.Text = SliderMax.Value.ToString("F1");
            RecolorPoints();
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

        private void AutoFitCamera(Vector3[] positions)
        {
            if (positions.Length == 0) return;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);

            foreach (var p in positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            var center = (min + max) * 0.5f;
            var extent = (max - min).Length();
            float distance = extent * 1.5f;

            MainCamera.Position = new System.Windows.Media.Media3D.Point3D(
                center.X + distance * 0.5,
                center.Y + distance * 0.5,
                center.Z + distance * 0.5);
            MainCamera.LookDirection = new System.Windows.Media.Media3D.Vector3D(
                center.X - MainCamera.Position.X,
                center.Y - MainCamera.Position.Y,
                center.Z - MainCamera.Position.Z);
            MainCamera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
        }

        #endregion

        #region Camera Presets

        private void BtnResetView_Click(object sender, RoutedEventArgs e)
        {
            MainCamera.Position = new System.Windows.Media.Media3D.Point3D(200, 200, 200);
            MainCamera.LookDirection = new System.Windows.Media.Media3D.Vector3D(-200, -200, -200);
            MainCamera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);

            if (PointCloud != null && PointCloud.Positions.Length > 0)
                AutoFitCamera(PointCloud.Positions);
        }

        private void BtnTopView_Click(object sender, RoutedEventArgs e)
        {
            var center = GetCloudCenter();
            float dist = GetViewDistance();
            MainCamera.Position = new System.Windows.Media.Media3D.Point3D(center.X, center.Y + dist, center.Z);
            MainCamera.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, -1, 0);
            MainCamera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, -1);
        }

        private void BtnFrontView_Click(object sender, RoutedEventArgs e)
        {
            var center = GetCloudCenter();
            float dist = GetViewDistance();
            MainCamera.Position = new System.Windows.Media.Media3D.Point3D(center.X, center.Y, center.Z + dist);
            MainCamera.LookDirection = new System.Windows.Media.Media3D.Vector3D(0, 0, -1);
            MainCamera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
        }

        private void BtnSideView_Click(object sender, RoutedEventArgs e)
        {
            var center = GetCloudCenter();
            float dist = GetViewDistance();
            MainCamera.Position = new System.Windows.Media.Media3D.Point3D(center.X + dist, center.Y, center.Z);
            MainCamera.LookDirection = new System.Windows.Media.Media3D.Vector3D(-1, 0, 0);
            MainCamera.UpDirection = new System.Windows.Media.Media3D.Vector3D(0, 1, 0);
        }

        private Vector3 GetCloudCenter()
        {
            if (PointCloud == null || PointCloud.Positions.Length == 0)
                return Vector3.Zero;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in PointCloud.Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            return (min + max) * 0.5f;
        }

        private float GetViewDistance()
        {
            if (PointCloud == null || PointCloud.Positions.Length == 0)
                return 300f;

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in PointCloud.Positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            return (max - min).Length() * 1.5f;
        }

        #endregion
    }
}
