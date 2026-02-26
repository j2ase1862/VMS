using System;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.ViewModels;

namespace VMS.VisionSetup.Views.Sequence
{
    public partial class SequenceEditorView : UserControl
    {
        // 노드 드래그 상태
        private bool _isDraggingNode;
        private SequenceNodeItem? _draggingNode;
        private Point _dragStartPoint;
        private double _dragStartX, _dragStartY;

        public SequenceEditorView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            DataContextChanged += OnDataContextChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // 초기 연결선 그리기
            UpdateEdgeLines();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 이전 ViewModel의 이벤트 해제
            if (e.OldValue is SequenceEditorViewModel oldVm)
            {
                oldVm.Edges.CollectionChanged -= OnEdgesCollectionChanged;
                oldVm.Nodes.CollectionChanged -= OnNodesCollectionChanged;
            }

            // 새 ViewModel의 컬렉션 변경 이벤트 구독
            if (e.NewValue is SequenceEditorViewModel newVm)
            {
                newVm.Edges.CollectionChanged += OnEdgesCollectionChanged;
                newVm.Nodes.CollectionChanged += OnNodesCollectionChanged;
            }
        }

        private void OnEdgesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 엣지 추가/삭제 시 연결선 다시 그리기 (컨테이너 생성 후 실행되도록 딜레이)
            Dispatcher.BeginInvoke(new Action(() => UpdateEdgeLines()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void OnNodesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 노드 추가/삭제 시에도 연결선 위치 갱신
            Dispatcher.BeginInvoke(new Action(() => UpdateEdgeLines()),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private SequenceEditorViewModel? ViewModel => DataContext as SequenceEditorViewModel;

        // --- 팔레트 드래그 ---

        private void Palette_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is NodePaletteItem paletteItem)
            {
                var data = new DataObject("PaletteItem", paletteItem);
                DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
            }
        }

        private void Canvas_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("PaletteItem"))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Canvas_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("PaletteItem") && ViewModel != null)
            {
                var paletteItem = e.Data.GetData("PaletteItem") as NodePaletteItem;
                if (paletteItem == null) return;

                var pos = e.GetPosition(SequenceCanvas);
                ViewModel.AddNodeCommand.Execute(paletteItem);

                // 드롭 위치에 마지막 추가된 노드 배치
                if (ViewModel.Nodes.Count > 0)
                {
                    var lastNode = ViewModel.Nodes[^1];
                    lastNode.X = pos.X - 60;
                    lastNode.Y = pos.Y - 20;
                }
            }
        }

        // --- 노드 드래그 이동 ---

        private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is SequenceNodeItem node)
            {
                // 연결 모드 중이면 대상 클릭
                if (ViewModel?.IsConnecting == true)
                {
                    ViewModel.CompleteConnectionCommand.Execute(node);
                    e.Handled = true;
                    return;
                }

                // 노드 선택
                if (ViewModel != null)
                {
                    foreach (var n in ViewModel.Nodes)
                        n.IsSelected = false;
                    node.IsSelected = true;
                    ViewModel.SelectedNode = node;
                }

                // 드래그 시작
                _isDraggingNode = true;
                _draggingNode = node;
                _dragStartPoint = e.GetPosition(SequenceCanvas);
                _dragStartX = node.X;
                _dragStartY = node.Y;
                fe.CaptureMouse();
                e.Handled = true;
            }
        }

        private void Node_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDraggingNode && _draggingNode != null && e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPos = e.GetPosition(SequenceCanvas);
                _draggingNode.X = _dragStartX + (currentPos.X - _dragStartPoint.X);
                _draggingNode.Y = _dragStartY + (currentPos.Y - _dragStartPoint.Y);

                // 캔버스 범위 제한
                if (_draggingNode.X < 0) _draggingNode.X = 0;
                if (_draggingNode.Y < 0) _draggingNode.Y = 0;

                UpdateEdgeLines();
                e.Handled = true;
            }
        }

        private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDraggingNode)
            {
                _isDraggingNode = false;
                _draggingNode = null;
                if (sender is FrameworkElement fe)
                    fe.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void Node_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 우클릭 시 해당 노드 선택
            if (sender is FrameworkElement fe && fe.DataContext is SequenceNodeItem node && ViewModel != null)
            {
                foreach (var n in ViewModel.Nodes)
                    n.IsSelected = false;
                node.IsSelected = true;
                ViewModel.SelectedNode = node;
            }
        }

        // --- 캔버스 클릭 (선택 해제 / 연결 취소) ---

        private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ViewModel == null) return;

            if (ViewModel.IsConnecting)
            {
                ViewModel.CancelConnectionCommand.Execute(null);
            }
            else
            {
                foreach (var n in ViewModel.Nodes)
                    n.IsSelected = false;
                ViewModel.SelectedNode = null;
            }
        }

        // --- 엣지 우클릭 ---
        private void Edge_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        // --- 연결선 그리기 ---

        /// <summary>
        /// 모든 엣지의 Bezier 경로를 업데이트
        /// </summary>
        public void UpdateEdgeLines()
        {
            if (ViewModel == null) return;

            // ItemsControl에서 Path 요소를 직접 찾아서 업데이트
            Dispatcher.BeginInvoke(new Action(() =>
            {
                for (int i = 0; i < ViewModel.Edges.Count; i++)
                {
                    var edge = ViewModel.Edges[i];
                    if (edge.SourceNode == null || edge.TargetNode == null) continue;

                    // 소스/타겟 중심 좌표 계산 (노드 크기 추정: 120x40)
                    const double nodeW = 120, nodeH = 40;
                    double sx = edge.SourceNode.X + nodeW / 2;
                    double sy = edge.SourceNode.Y + nodeH;
                    double tx = edge.TargetNode.X + nodeW / 2;
                    double ty = edge.TargetNode.Y;

                    // Bezier 커브
                    double midY = (sy + ty) / 2;
                    var geometry = new PathGeometry();
                    var figure = new PathFigure { StartPoint = new Point(sx, sy) };
                    figure.Segments.Add(new BezierSegment(
                        new Point(sx, midY),
                        new Point(tx, midY),
                        new Point(tx, ty),
                        true));
                    geometry.Figures.Add(figure);

                    // 화살표 마커
                    double arrowSize = 6;
                    var arrowGeometry = new PathGeometry();
                    var arrowFigure = new PathFigure { StartPoint = new Point(tx - arrowSize, ty - arrowSize) };
                    arrowFigure.Segments.Add(new LineSegment(new Point(tx, ty), true));
                    arrowFigure.Segments.Add(new LineSegment(new Point(tx + arrowSize, ty - arrowSize), true));
                    arrowGeometry.Figures.Add(arrowFigure);

                    var combinedGeometry = new GeometryGroup();
                    combinedGeometry.Children.Add(geometry);
                    combinedGeometry.Children.Add(arrowGeometry);

                    // ItemsControl에서 해당 인덱스의 컨테이너 찾기
                    var container = EdgesControl.ItemContainerGenerator.ContainerFromIndex(i) as ContentPresenter;
                    if (container != null)
                    {
                        var path = FindChild<Path>(container, "EdgePath");
                        if (path != null)
                        {
                            path.Data = combinedGeometry;
                        }

                        var label = FindChild<System.Windows.Controls.TextBlock>(container, "EdgeLabel");
                        if (label != null)
                        {
                            Canvas.SetLeft(label, (sx + tx) / 2 - 15);
                            Canvas.SetTop(label, midY - 8);
                        }
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private static T? FindChild<T>(DependencyObject parent, string name) where T : FrameworkElement
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T fe && fe.Name == name)
                    return fe;
                var result = FindChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }
    }

    /// <summary>
    /// null → Collapsed, non-null → Visible
    /// </summary>
    public class NullToCollapsedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value != null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// null → Visible, non-null → Collapsed (inverse)
    /// </summary>
    public class NullToCollapsedInverseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value == null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// bool → 연결 버튼 텍스트 (true="연결 해제", false="PLC 연결")
    /// </summary>
    public class ConnectButtonTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? "연결 해제" : "PLC 연결";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// bool (IsPlcConnected) → 상태 색상 (true="#4CAF50" 초록, false="#F44336" 빨강)
    /// </summary>
    public class PlcStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? "#4CAF50" : "#666666";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// string (hex color) → SolidColorBrush (ConditionIndicatorColor 바인딩용)
    /// </summary>
    public class ConditionStatusColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string hex)
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
                catch { }
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// bool? → Visibility (HasValue=Visible, null=Collapsed)
    /// </summary>
    public class NullableBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
