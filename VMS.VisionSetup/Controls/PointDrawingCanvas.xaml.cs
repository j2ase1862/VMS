using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Controls
{
    public partial class PointDrawingCanvas : UserControl
    {
        private DefaultEffectsManager? _effectsManager;

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
                return;
            }

            var positions = data.Positions;
            var colors = data.Colors;

            var pointPositions = new Vector3Collection(positions.Length);
            var pointColors = new Color4Collection(positions.Length);

            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                pointPositions.Add(new Vector3(p.X, p.Y, p.Z));

                if (i < colors.Length)
                {
                    var c = colors[i];
                    pointColors.Add(new Color4(c.R / 255f, c.G / 255f, c.B / 255f, 1f));
                }
                else
                {
                    pointColors.Add(new Color4(1, 1, 1, 1));
                }
            }

            var indices = new IntCollection(positions.Length);
            for (int i = 0; i < positions.Length; i++)
                indices.Add(i);

            var geometry = new PointGeometry3D
            {
                Positions = pointPositions,
                Colors = pointColors,
                Indices = indices
            };

            PointCloudModel.Geometry = geometry;

            PointCountText.Text = $"{positions.Length:N0} points";
            StatusText.Text = $"Loaded: {data.Name} ({positions.Length:N0} points)";

            AutoFitCamera(positions);
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
