using VMS.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Collections.ObjectModel;

namespace VMS.Controls
{
    public partial class CameraDisplayControl : UserControl
    {
        private bool _isDragging;
        private Point _mouseOffsetInControl;
        private Canvas? _cameraCanvas;
        private Canvas? _alignmentGuideCanvas;
        private ObservableCollection<CameraViewModel>? _allCameras;

        private const double SnapThreshold = 8.0;
        private static readonly Brush GuideLineBrush = new SolidColorBrush(Color.FromRgb(0x00, 0xBC, 0xD4)); // Cyan

        public CameraDisplayControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Find the main CameraCanvas
            _cameraCanvas = FindCameraCanvas(this);

            // Find the AlignmentGuideCanvas (sibling of the ItemsControl inside CameraCanvas)
            if (_cameraCanvas != null)
            {
                foreach (var child in LogicalTreeHelper.GetChildren(_cameraCanvas))
                {
                    if (child is Canvas c && c.Name == "AlignmentGuideCanvas")
                    {
                        _alignmentGuideCanvas = c;
                        break;
                    }
                }
            }

            // Get camera collection from MainViewModel
            var window = Window.GetWindow(this);
            if (window?.DataContext is MainViewModel mainVm)
            {
                _allCameras = mainVm.Cameras;
            }

            // Setup drag behavior
            DragHandle.MouseLeftButtonDown += OnDragStart;
            DragHandle.MouseMove += OnDragMove;
            DragHandle.MouseLeftButtonUp += OnDragEnd;

            // Setup resize behavior
            ResizeGrip.DragDelta += OnResizeDelta;
            ResizeGrip.DragCompleted += OnResizeCompleted;

            // Setup settings button click
            SettingsButton.Click += OnSettingsClick;
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            // Find MainWindow and open settings panel for this camera
            var window = Window.GetWindow(this);
            if (window?.DataContext is MainViewModel mainVm && DataContext is CameraViewModel camVm)
            {
                mainVm.SelectCameraCommand.Execute(camVm);
            }
            e.Handled = true;
        }

        private static Canvas? FindCameraCanvas(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null)
            {
                if (parent is Canvas canvas && canvas.Name == "CameraCanvas")
                    return canvas;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return null;
        }

        private void OnDragStart(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is CameraViewModel vm)
            {
                _isDragging = true;
                // Remember where in the control the mouse clicked
                _mouseOffsetInControl = e.GetPosition(this);
                DragHandle.CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnDragMove(object sender, MouseEventArgs e)
        {
            if (_isDragging && DataContext is CameraViewModel vm && _cameraCanvas != null)
            {
                // Get mouse position relative to CameraCanvas
                var mouseInCanvas = e.GetPosition(_cameraCanvas);

                // Calculate where the control should be positioned
                var newX = mouseInCanvas.X - _mouseOffsetInControl.X;
                var newY = mouseInCanvas.Y - _mouseOffsetInControl.Y;

                // Clamp to canvas bounds (keep control visible)
                var maxX = _cameraCanvas.ActualWidth - vm.Width;
                var maxY = _cameraCanvas.ActualHeight - vm.Height;

                newX = Math.Max(0, Math.Min(maxX, newX));
                newY = Math.Max(0, Math.Min(maxY, newY));

                // Apply snap alignment and draw guide lines
                ApplySnapAlignment(vm, ref newX, ref newY);

                vm.X = newX;
                vm.Y = newY;

                // Update position on canvas
                var contentPresenter = VisualTreeHelper.GetParent(this) as ContentPresenter;
                if (contentPresenter != null)
                {
                    Canvas.SetLeft(contentPresenter, vm.X);
                    Canvas.SetTop(contentPresenter, vm.Y);
                }
            }
        }

        private void OnDragEnd(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                DragHandle.ReleaseMouseCapture();
                ClearGuideLines();
            }
        }

        private void ApplySnapAlignment(CameraViewModel draggedVm, ref double newX, ref double newY)
        {
            ClearGuideLines();

            if (_allCameras == null || _alignmentGuideCanvas == null || _cameraCanvas == null)
                return;

            double canvasW = _cameraCanvas.ActualWidth;
            double canvasH = _cameraCanvas.ActualHeight;

            // Dragged control edges
            double dragTop = newY;
            double dragBottom = newY + draggedVm.Height;
            double dragLeft = newX;
            double dragRight = newX + draggedVm.Width;

            // Track best snap for each axis (closest within threshold)
            double? bestSnapY = null;
            double bestSnapYDist = SnapThreshold + 1;
            double? bestSnapYLine = null;

            double? bestSnapBottomY = null;
            double bestSnapBottomDist = SnapThreshold + 1;
            double? bestSnapBottomLine = null;

            double? bestSnapX = null;
            double bestSnapXDist = SnapThreshold + 1;
            double? bestSnapXLine = null;

            double? bestSnapRightX = null;
            double bestSnapRightDist = SnapThreshold + 1;
            double? bestSnapRightLine = null;

            foreach (var other in _allCameras)
            {
                if (ReferenceEquals(other, draggedVm))
                    continue;

                double otherTop = other.Y;
                double otherBottom = other.Y + other.Height;
                double otherLeft = other.X;
                double otherRight = other.X + other.Width;

                // --- Horizontal alignment (Y axis) ---

                // Dragged Top vs Other Top
                double dist = Math.Abs(dragTop - otherTop);
                if (dist < SnapThreshold && dist < bestSnapYDist)
                {
                    bestSnapYDist = dist;
                    bestSnapY = otherTop;
                    bestSnapYLine = otherTop;
                }

                // Dragged Top vs Other Bottom
                dist = Math.Abs(dragTop - otherBottom);
                if (dist < SnapThreshold && dist < bestSnapYDist)
                {
                    bestSnapYDist = dist;
                    bestSnapY = otherBottom;
                    bestSnapYLine = otherBottom;
                }

                // Dragged Bottom vs Other Top
                dist = Math.Abs(dragBottom - otherTop);
                if (dist < SnapThreshold && dist < bestSnapBottomDist)
                {
                    bestSnapBottomDist = dist;
                    bestSnapBottomY = otherTop - draggedVm.Height;
                    bestSnapBottomLine = otherTop;
                }

                // Dragged Bottom vs Other Bottom
                dist = Math.Abs(dragBottom - otherBottom);
                if (dist < SnapThreshold && dist < bestSnapBottomDist)
                {
                    bestSnapBottomDist = dist;
                    bestSnapBottomY = otherBottom - draggedVm.Height;
                    bestSnapBottomLine = otherBottom;
                }

                // --- Vertical alignment (X axis) ---

                // Dragged Left vs Other Left
                dist = Math.Abs(dragLeft - otherLeft);
                if (dist < SnapThreshold && dist < bestSnapXDist)
                {
                    bestSnapXDist = dist;
                    bestSnapX = otherLeft;
                    bestSnapXLine = otherLeft;
                }

                // Dragged Left vs Other Right
                dist = Math.Abs(dragLeft - otherRight);
                if (dist < SnapThreshold && dist < bestSnapXDist)
                {
                    bestSnapXDist = dist;
                    bestSnapX = otherRight;
                    bestSnapXLine = otherRight;
                }

                // Dragged Right vs Other Left
                dist = Math.Abs(dragRight - otherLeft);
                if (dist < SnapThreshold && dist < bestSnapRightDist)
                {
                    bestSnapRightDist = dist;
                    bestSnapRightX = otherLeft - draggedVm.Width;
                    bestSnapRightLine = otherLeft;
                }

                // Dragged Right vs Other Right
                dist = Math.Abs(dragRight - otherRight);
                if (dist < SnapThreshold && dist < bestSnapRightDist)
                {
                    bestSnapRightDist = dist;
                    bestSnapRightX = otherRight - draggedVm.Width;
                    bestSnapRightLine = otherRight;
                }
            }

            // Apply the closest Y snap (top edge takes priority over bottom if both match)
            if (bestSnapY.HasValue && bestSnapYDist <= bestSnapBottomDist)
            {
                newY = bestSnapY.Value;
                DrawHorizontalGuideLine(bestSnapYLine!.Value, canvasW);
            }
            else if (bestSnapBottomY.HasValue)
            {
                newY = bestSnapBottomY.Value;
                DrawHorizontalGuideLine(bestSnapBottomLine!.Value, canvasW);
            }

            // Both top and bottom can also show simultaneously if they match different edges
            if (bestSnapY.HasValue && bestSnapBottomY.HasValue
                && bestSnapYDist <= SnapThreshold && bestSnapBottomDist <= SnapThreshold
                && bestSnapYLine != bestSnapBottomLine)
            {
                // Already drew one line above, draw the other
                if (bestSnapYDist <= bestSnapBottomDist)
                    DrawHorizontalGuideLine(bestSnapBottomLine!.Value, canvasW);
                else
                    DrawHorizontalGuideLine(bestSnapYLine!.Value, canvasW);
            }

            // Apply the closest X snap (left edge takes priority over right if both match)
            if (bestSnapX.HasValue && bestSnapXDist <= bestSnapRightDist)
            {
                newX = bestSnapX.Value;
                DrawVerticalGuideLine(bestSnapXLine!.Value, canvasH);
            }
            else if (bestSnapRightX.HasValue)
            {
                newX = bestSnapRightX.Value;
                DrawVerticalGuideLine(bestSnapRightLine!.Value, canvasH);
            }

            // Both left and right can also show simultaneously
            if (bestSnapX.HasValue && bestSnapRightX.HasValue
                && bestSnapXDist <= SnapThreshold && bestSnapRightDist <= SnapThreshold
                && bestSnapXLine != bestSnapRightLine)
            {
                if (bestSnapXDist <= bestSnapRightDist)
                    DrawVerticalGuideLine(bestSnapRightLine!.Value, canvasH);
                else
                    DrawVerticalGuideLine(bestSnapXLine!.Value, canvasH);
            }
        }

        private void DrawHorizontalGuideLine(double y, double canvasWidth)
        {
            if (_alignmentGuideCanvas == null) return;

            var line = new Line
            {
                X1 = 0,
                Y1 = y,
                X2 = canvasWidth,
                Y2 = y,
                Stroke = GuideLineBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 6, 3 },
                SnapsToDevicePixels = true
            };
            line.SetValue(RenderOptions.EdgeModeProperty, EdgeMode.Aliased);
            _alignmentGuideCanvas.Children.Add(line);
        }

        private void DrawVerticalGuideLine(double x, double canvasHeight)
        {
            if (_alignmentGuideCanvas == null) return;

            var line = new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = canvasHeight,
                Stroke = GuideLineBrush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 6, 3 },
                SnapsToDevicePixels = true
            };
            line.SetValue(RenderOptions.EdgeModeProperty, EdgeMode.Aliased);
            _alignmentGuideCanvas.Children.Add(line);
        }

        private void ClearGuideLines()
        {
            _alignmentGuideCanvas?.Children.Clear();
        }

        private void OnResizeDelta(object sender, DragDeltaEventArgs e)
        {
            if (DataContext is CameraViewModel vm && _cameraCanvas != null)
            {
                var newWidth = Math.Max(200, vm.Width + e.HorizontalChange);
                var newHeight = Math.Max(150, vm.Height + e.VerticalChange);

                // Clamp size to not exceed canvas bounds
                var maxWidth = _cameraCanvas.ActualWidth - vm.X;
                var maxHeight = _cameraCanvas.ActualHeight - vm.Y;

                newWidth = Math.Min(newWidth, maxWidth);
                newHeight = Math.Min(newHeight, maxHeight);

                // Apply resize snap alignment
                ApplyResizeSnapAlignment(vm, ref newWidth, ref newHeight);

                vm.Width = newWidth;
                vm.Height = newHeight;

                Width = vm.Width;
                Height = vm.Height;
            }
        }

        private void OnResizeCompleted(object sender, DragCompletedEventArgs e)
        {
            ClearGuideLines();
        }

        private void ApplyResizeSnapAlignment(CameraViewModel resizingVm, ref double newWidth, ref double newHeight)
        {
            ClearGuideLines();

            if (_allCameras == null || _alignmentGuideCanvas == null || _cameraCanvas == null)
                return;

            double canvasW = _cameraCanvas.ActualWidth;
            double canvasH = _cameraCanvas.ActualHeight;

            // During resize, top-left is fixed; right and bottom edges move
            double rightEdge = resizingVm.X + newWidth;
            double bottomEdge = resizingVm.Y + newHeight;

            // Track best snap for right edge (X)
            double? bestSnapRightLine = null;
            double bestSnapRightDist = SnapThreshold + 1;
            double bestSnapRightWidth = newWidth;

            // Track best snap for bottom edge (Y)
            double? bestSnapBottomLine = null;
            double bestSnapBottomDist = SnapThreshold + 1;
            double bestSnapBottomHeight = newHeight;

            foreach (var other in _allCameras)
            {
                if (ReferenceEquals(other, resizingVm))
                    continue;

                double otherTop = other.Y;
                double otherBottom = other.Y + other.Height;
                double otherLeft = other.X;
                double otherRight = other.X + other.Width;

                // --- Right edge snap ---

                // Right vs Other Left
                double dist = Math.Abs(rightEdge - otherLeft);
                if (dist < SnapThreshold && dist < bestSnapRightDist)
                {
                    bestSnapRightDist = dist;
                    bestSnapRightWidth = otherLeft - resizingVm.X;
                    bestSnapRightLine = otherLeft;
                }

                // Right vs Other Right
                dist = Math.Abs(rightEdge - otherRight);
                if (dist < SnapThreshold && dist < bestSnapRightDist)
                {
                    bestSnapRightDist = dist;
                    bestSnapRightWidth = otherRight - resizingVm.X;
                    bestSnapRightLine = otherRight;
                }

                // --- Bottom edge snap ---

                // Bottom vs Other Top
                dist = Math.Abs(bottomEdge - otherTop);
                if (dist < SnapThreshold && dist < bestSnapBottomDist)
                {
                    bestSnapBottomDist = dist;
                    bestSnapBottomHeight = otherTop - resizingVm.Y;
                    bestSnapBottomLine = otherTop;
                }

                // Bottom vs Other Bottom
                dist = Math.Abs(bottomEdge - otherBottom);
                if (dist < SnapThreshold && dist < bestSnapBottomDist)
                {
                    bestSnapBottomDist = dist;
                    bestSnapBottomHeight = otherBottom - resizingVm.Y;
                    bestSnapBottomLine = otherBottom;
                }
            }

            // Apply right edge snap
            if (bestSnapRightLine.HasValue && bestSnapRightWidth >= 200)
            {
                newWidth = bestSnapRightWidth;
                DrawVerticalGuideLine(bestSnapRightLine.Value, canvasH);
            }

            // Apply bottom edge snap
            if (bestSnapBottomLine.HasValue && bestSnapBottomHeight >= 150)
            {
                newHeight = bestSnapBottomHeight;
                DrawHorizontalGuideLine(bestSnapBottomLine.Value, canvasW);
            }
        }
    }
}
