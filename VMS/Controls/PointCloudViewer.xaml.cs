using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using VMS.Camera.Models;

namespace VMS.Controls
{
    public partial class PointCloudViewer : UserControl
    {
        private DefaultEffectsManager? _effectsManager;

        #region Dependency Properties

        public static readonly DependencyProperty PointCloudProperty =
            DependencyProperty.Register(
                nameof(PointCloud),
                typeof(PointCloudData),
                typeof(PointCloudViewer),
                new PropertyMetadata(null, OnPointCloudChanged));

        public PointCloudData? PointCloud
        {
            get => (PointCloudData?)GetValue(PointCloudProperty);
            set => SetValue(PointCloudProperty, value);
        }

        #endregion

        public PointCloudViewer()
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

            builder.AddLine(Vector3.Zero, new Vector3(len, 0, 0));
            builder.AddLine(Vector3.Zero, new Vector3(0, len, 0));
            builder.AddLine(Vector3.Zero, new Vector3(0, 0, len));

            var geo = builder.ToLineGeometry3D();

            var axisColors = new Color4Collection
            {
                new Color4(1, 0, 0, 1), new Color4(1, 0, 0, 1),
                new Color4(0, 1, 0, 1), new Color4(0, 1, 0, 1),
                new Color4(0, 0, 1, 1), new Color4(0, 0, 1, 1)
            };
            geo.Colors = axisColors;

            AxisLines.Geometry = geo;
        }

        #endregion

        #region Point Cloud Rendering

        private static void OnPointCloudChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PointCloudViewer viewer)
            {
                viewer.UpdatePointCloud();
            }
        }

        private void UpdatePointCloud()
        {
            var data = PointCloud;
            if (data == null || data.Positions.Length == 0)
            {
                PointCloudModel.Geometry = null;
                PointCountText.Text = "No points";
                ColorBarPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var positions = data.Positions;

            float yMin = float.MaxValue;
            float yMax = float.MinValue;
            foreach (var p in positions)
            {
                if (p.Y < yMin) yMin = p.Y;
                if (p.Y > yMax) yMax = p.Y;
            }

            MinValueLabel.Text = yMin.ToString("F1");
            MaxValueLabel.Text = yMax.ToString("F1");

            var pointPositions = new Vector3Collection(positions.Length);
            for (int i = 0; i < positions.Length; i++)
            {
                var p = positions[i];
                pointPositions.Add(new Vector3(p.X, p.Y, p.Z));
            }

            var indices = new IntCollection(positions.Length);
            for (int i = 0; i < positions.Length; i++)
                indices.Add(i);

            var pointColors = BuildJetColors(positions, yMin, yMax);

            var geometry = new PointGeometry3D
            {
                Positions = pointPositions,
                Colors = pointColors,
                Indices = indices
            };

            PointCloudModel.Geometry = geometry;

            ColorBarPanel.Visibility = Visibility.Visible;
            PointCountText.Text = $"{positions.Length:N0} pts";

            AutoFitCamera(positions);
        }

        #endregion

        #region Jet Colormap

        private static Color4 JetColormap(float t)
        {
            t = Math.Clamp(t, 0f, 1f);

            float r, g, b;

            if (t < 0.25f)
            {
                float s = t / 0.25f;
                r = 0f; g = s; b = 1f;
            }
            else if (t < 0.5f)
            {
                float s = (t - 0.25f) / 0.25f;
                r = 0f; g = 1f; b = 1f - s;
            }
            else if (t < 0.75f)
            {
                float s = (t - 0.5f) / 0.25f;
                r = s; g = 1f; b = 0f;
            }
            else
            {
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

        #endregion

        #region Camera Controls

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

        #endregion
    }
}
