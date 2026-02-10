using VMS.VisionSetup.ViewModels.ToolSettings;
using VMS.VisionSetup.VisionTools.Measurement;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VMS.VisionSetup.Views.ToolSettings.Tools
{
    public partial class CaliperToolSettings : UserControl
    {
        private CaliperToolSettingsViewModel? _subscribedVM;

        public CaliperToolSettings()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is CaliperToolSettingsViewModel vm)
            {
                _subscribedVM = vm;
                vm.PropertyChanged += VM_PropertyChanged;
                DrawProfileGraph();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_subscribedVM != null)
            {
                _subscribedVM.PropertyChanged -= VM_PropertyChanged;
                _subscribedVM = null;
            }
        }

        private void VM_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CaliperToolSettingsViewModel.LastProfile) ||
                e.PropertyName == nameof(CaliperToolSettingsViewModel.LastGradient) ||
                e.PropertyName == nameof(CaliperToolSettingsViewModel.EdgeThreshold))
            {
                Dispatcher.Invoke(DrawProfileGraph);
            }
        }

        private void ProfileCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawProfileGraph();
        }

        private void DrawProfileGraph()
        {
            ProfileCanvas.Children.Clear();

            var vm = _subscribedVM;
            var profile = vm?.LastProfile;
            var gradient = vm?.LastGradient;

            if (profile == null || profile.Length < 2)
            {
                ProfilePlaceholder.Visibility = Visibility.Visible;
                return;
            }

            ProfilePlaceholder.Visibility = Visibility.Collapsed;

            double w = ProfileCanvas.ActualWidth;
            double h = ProfileCanvas.ActualHeight;
            if (w <= 0 || h <= 0) return;

            int len = profile.Length;
            double halfH = h / 2.0;
            const double pad = 4;

            // --- Profile line (top half) ---
            double pMin = double.MaxValue, pMax = double.MinValue;
            for (int i = 0; i < len; i++)
            {
                if (profile[i] < pMin) pMin = profile[i];
                if (profile[i] > pMax) pMax = profile[i];
            }
            double pRange = pMax - pMin;
            if (pRange < 1) pRange = 1;

            var profilePts = new PointCollection(len);
            for (int i = 0; i < len; i++)
            {
                double x = (double)i / (len - 1) * w;
                double y = pad + (halfH - 2 * pad) * (1.0 - (profile[i] - pMin) / pRange);
                profilePts.Add(new Point(x, y));
            }
            ProfileCanvas.Children.Add(new Polyline
            {
                Points = profilePts,
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0C0C0")),
                StrokeThickness = 1
            });

            // --- Divider line ---
            ProfileCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = halfH, X2 = w, Y2 = halfH,
                Stroke = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
                StrokeThickness = 1
            });

            // --- Gradient line (bottom half) ---
            if (gradient != null && gradient.Length >= 2)
            {
                double gMax = 0;
                for (int i = 0; i < gradient.Length; i++)
                {
                    double a = Math.Abs(gradient[i]);
                    if (a > gMax) gMax = a;
                }
                if (gMax < 1) gMax = 1;

                double gradCenter = halfH + (h - halfH) / 2.0;
                double gradHalfRange = (h - halfH) / 2.0 - pad;

                var gradPts = new PointCollection(gradient.Length);
                for (int i = 0; i < gradient.Length; i++)
                {
                    double x = (double)i / (len - 1) * w;
                    double y = gradCenter - (gradient[i] / gMax) * gradHalfRange;
                    gradPts.Add(new Point(x, y));
                }
                ProfileCanvas.Children.Add(new Polyline
                {
                    Points = gradPts,
                    Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00BFFF")),
                    StrokeThickness = 1
                });

                // --- Threshold lines (dashed red) ---
                double threshold = vm!.EdgeThreshold;
                if (threshold <= gMax)
                {
                    double threshPos = gradCenter - (threshold / gMax) * gradHalfRange;
                    double threshNeg = gradCenter + (threshold / gMax) * gradHalfRange;
                    var threshBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5555"));

                    ProfileCanvas.Children.Add(new Line
                    {
                        X1 = 0, Y1 = threshPos, X2 = w, Y2 = threshPos,
                        Stroke = threshBrush,
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection(new[] { 4.0, 2.0 })
                    });
                    ProfileCanvas.Children.Add(new Line
                    {
                        X1 = 0, Y1 = threshNeg, X2 = w, Y2 = threshNeg,
                        Stroke = threshBrush.Clone(),
                        StrokeThickness = 1,
                        StrokeDashArray = new DoubleCollection(new[] { 4.0, 2.0 })
                    });
                }
            }

            // --- Edge markers (green vertical lines) ---
            if (vm?.LastResult?.Data?.ContainsKey("Edges") == true &&
                vm.LastResult.Data["Edges"] is List<EdgeResult> edges)
            {
                var edgeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00FF00"));
                foreach (var edge in edges)
                {
                    double x = edge.SubPixelPosition / (len - 1) * w;
                    ProfileCanvas.Children.Add(new Line
                    {
                        X1 = x, Y1 = 0, X2 = x, Y2 = h,
                        Stroke = edgeBrush,
                        StrokeThickness = 1,
                        Opacity = 0.7
                    });
                }
            }
        }
    }
}
