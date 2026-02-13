using VMS.VisionSetup.Controls;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.Views;
using VMS.VisionSetup.Views.Camera;
using VMS.VisionSetup.Views.Recipe;
using VMS.VisionSetup.VisionTools.PatternMatching;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VMS.VisionSetup
{
    /// <summary>
    /// MainView.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainView : System.Windows.Window
    {
        private bool _isDragging = false;
        private System.Windows.Point _mouseOffset;
        private bool _isDrawingSearchRegion = false;

        // 연결선 관련 필드
        private bool _isConnectionMode = false;
        private ConnectionType _pendingConnectionType;
        private ToolItem? _connectionSourceTool = null;
        private Line? _tempConnectionLine = null;

        public MainView()
        {
            InitializeComponent();
            var vm = new MainViewModel();
            DataContext = vm;

            // If recipe was pre-loaded via command-line argument, update UI
            var preloadedRecipe = RecipeService.Instance.CurrentRecipe;
            if (preloadedRecipe != null)
            {
                vm.CurrentRecipeName = preloadedRecipe.Name;
                vm.StatusMessage = $"Recipe loaded: {preloadedRecipe.Name}";
            }

            // 연결 컬렉션 변경 감지
            vm.Connections.CollectionChanged += Connections_CollectionChanged;

            // 도구 위치 변경 시 연결선 업데이트를 위한 이벤트 등록
            vm.DroppedTools.CollectionChanged += DroppedTools_CollectionChanged;

            // ROI 동기화 이벤트 구독
            vm.RequestShowToolROI += (s, roi) =>
            {
                var ft = vm.SelectedVisionTool as FeatureMatchTool;
                var searchRoi = (ft != null && ft.UseSearchRegion) ? ft.AssociatedSearchRegionShape : null;
                ImageCanvasControl.ShowToolROIs(roi, searchRoi);
            };
            vm.RequestRefreshROI += (s, roi) => ImageCanvasControl.RefreshROIVisual(roi);

            // View-level messages via WeakReferenceMessenger
            WeakReferenceMessenger.Default.Register<RequestDrawROIMessage>(this, (r, m) =>
            {
                _isDrawingSearchRegion = false;
                ImageCanvasControl.ActivateDrawingMode(EditMode.DrawRectangle);
            });
            WeakReferenceMessenger.Default.Register<RequestClearROIMessage>(this, (r, m) =>
            {
                _isDrawingSearchRegion = false;
                var ft = vm.SelectedVisionTool as FeatureMatchTool;
                ImageCanvasControl.ShowToolROIs(null, ft?.AssociatedSearchRegionShape);
            });
            WeakReferenceMessenger.Default.Register<RequestDrawSearchRegionMessage>(this, (r, m) =>
            {
                _isDrawingSearchRegion = true;
                ImageCanvasControl.ActivateDrawingMode(EditMode.DrawRectangle);
            });
            WeakReferenceMessenger.Default.Register<RequestClearSearchRegionMessage>(this, (r, m) =>
            {
                _isDrawingSearchRegion = false;
                vm.ClearSearchRegion();
            });
        }

        #region Tool Position Change Tracking

        /// <summary>
        /// DroppedTools 컬렉션 변경 시 위치 변경 이벤트 등록/해제
        /// </summary>
        private void DroppedTools_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (ToolItem tool in e.NewItems)
                {
                    tool.PropertyChanged += ToolItem_PropertyChanged;
                }
            }
            if (e.OldItems != null)
            {
                foreach (ToolItem tool in e.OldItems)
                {
                    tool.PropertyChanged -= ToolItem_PropertyChanged;
                }
            }

            // 도구 추가/제거 시 연결선 다시 그리기
            RedrawAllConnections();
        }

        /// <summary>
        /// 도구 아이템의 속성(좌표) 변경 시 연결선 업데이트
        /// </summary>
        private void ToolItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ToolItem.X) || e.PropertyName == nameof(ToolItem.Y))
            {
                RedrawAllConnections();
            }
        }

        #endregion

        #region Connection Line Drawing

        /// <summary>
        /// 연결 컬렉션 변경 시 연결선 다시 그리기
        /// </summary>
        private void Connections_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            RedrawAllConnections();
        }

        /// <summary>
        /// 모든 연결선 다시 그리기
        /// </summary>
        private void RedrawAllConnections()
        {
            ConnectionCanvas.Children.Clear();

            var vm = DataContext as MainViewModel;
            if (vm == null) return;

            foreach (var connection in vm.Connections)
            {
                DrawConnectionLine(connection);
            }
        }

        /// <summary>
        /// 단일 연결선 그리기
        /// </summary>
        private void DrawConnectionLine(ToolConnection connection)
        {
            if (connection.SourceToolItem == null || connection.TargetToolItem == null)
                return;

            // 도구 Border의 예상 크기 (탭 컨트롤: 헤더 + 바디)
            const double toolWidth = 150;
            const double toolHeight = 50;

            // Source 도구의 중심 좌표
            double sourceX = connection.SourceToolItem.X + toolWidth / 2;
            double sourceY = connection.SourceToolItem.Y + toolHeight / 2;

            // Target 도구의 중심 좌표
            double targetX = connection.TargetToolItem.X + toolWidth / 2;
            double targetY = connection.TargetToolItem.Y + toolHeight / 2;

            var brush = GetConnectionBrush(connection.Type);

            // 연결선 (메인 라인)
            var line = new Line
            {
                X1 = sourceX,
                Y1 = sourceY,
                X2 = targetX,
                Y2 = targetY,
                Stroke = brush,
                StrokeThickness = 2,
                StrokeDashArray = GetDashArray(connection.Type),
                IsHitTestVisible = false
            };
            ConnectionCanvas.Children.Add(line);

            // 화살표 머리 그리기
            DrawArrowHead(targetX, targetY, sourceX, sourceY, brush);

            // 연결 타입 라벨
            double midX = (sourceX + targetX) / 2;
            double midY = (sourceY + targetY) / 2;
            var label = new TextBlock
            {
                Text = connection.TypeDisplayName,
                Foreground = brush,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false,
                Background = new SolidColorBrush(Color.FromArgb(200, 30, 30, 30)),
                Padding = new Thickness(3, 1, 3, 1)
            };
            Canvas.SetLeft(label, midX - 20);
            Canvas.SetTop(label, midY - 10);
            ConnectionCanvas.Children.Add(label);
        }

        /// <summary>
        /// 화살표 머리 그리기
        /// </summary>
        private void DrawArrowHead(double tipX, double tipY, double tailX, double tailY, Brush brush)
        {
            double angle = Math.Atan2(tipY - tailY, tipX - tailX);
            double arrowLength = 12;
            double arrowWidth = Math.PI / 6; // 30도

            var point1 = new System.Windows.Point(
                tipX - arrowLength * Math.Cos(angle - arrowWidth),
                tipY - arrowLength * Math.Sin(angle - arrowWidth));

            var point2 = new System.Windows.Point(
                tipX - arrowLength * Math.Cos(angle + arrowWidth),
                tipY - arrowLength * Math.Sin(angle + arrowWidth));

            var polygon = new Polygon
            {
                Points = new PointCollection { new System.Windows.Point(tipX, tipY), point1, point2 },
                Fill = brush,
                IsHitTestVisible = false
            };
            ConnectionCanvas.Children.Add(polygon);
        }

        /// <summary>
        /// 연결 타입에 따른 브러시 반환
        /// </summary>
        private static SolidColorBrush GetConnectionBrush(ConnectionType type)
        {
            return type switch
            {
                ConnectionType.Image => new SolidColorBrush(Color.FromRgb(76, 175, 80)),        // 녹색
                ConnectionType.Coordinates => new SolidColorBrush(Color.FromRgb(33, 150, 243)),  // 파란색
                ConnectionType.Result => new SolidColorBrush(Color.FromRgb(255, 152, 0)),        // 주황색
                _ => new SolidColorBrush(Colors.White)
            };
        }

        /// <summary>
        /// 연결 타입에 따른 대시 배열 반환
        /// </summary>
        private static DoubleCollection GetDashArray(ConnectionType type)
        {
            return type switch
            {
                ConnectionType.Image => new DoubleCollection(),               // 실선
                ConnectionType.Coordinates => new DoubleCollection { 6, 3 },  // 긴 대시
                ConnectionType.Result => new DoubleCollection { 3, 3 },       // 짧은 대시
                _ => new DoubleCollection()
            };
        }

        #endregion

        #region Connection Mode (Context Menu → Click Target)

        /// <summary>
        /// 연결 메뉴 클릭 - 연결 모드 시작
        /// </summary>
        private void ConnectMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                // Context Menu의 PlacementTarget에서 소스 도구 가져오기
                var contextMenu = menuItem.Parent as ContextMenu;
                var border = contextMenu?.PlacementTarget as Border;
                var sourceTool = border?.DataContext as ToolItem;

                if (sourceTool == null) return;

                // 연결 타입 파싱
                string? tag = menuItem.Tag as string;
                if (tag == null) return;

                _pendingConnectionType = tag switch
                {
                    "Image" => ConnectionType.Image,
                    "Coordinates" => ConnectionType.Coordinates,
                    "Result" => ConnectionType.Result,
                    _ => ConnectionType.Image
                };

                // 연결 모드 시작
                _isConnectionMode = true;
                _connectionSourceTool = sourceTool;

                // 안내 텍스트 표시
                ConnectionModeHint.Text = $"[{_pendingConnectionType}] 연결 모드: 대상 도구를 클릭하세요 (ESC로 취소)";
                ConnectionModeHint.Visibility = Visibility.Visible;

                // ESC 키 핸들러 등록
                this.KeyDown += ConnectionMode_KeyDown;

                // 마우스 이벤트 (임시 연결선 그리기)
                WorkspaceArea.MouseMove += ConnectionMode_MouseMove;
            }
        }

        /// <summary>
        /// 연결 제거 메뉴 클릭
        /// </summary>
        private void RemoveConnectionsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                var contextMenu = menuItem.Parent as ContextMenu;
                var border = contextMenu?.PlacementTarget as Border;
                var tool = border?.DataContext as ToolItem;

                if (tool == null) return;

                var vm = DataContext as MainViewModel;
                vm?.RemoveConnectionsForTool(tool);
            }
        }

        /// <summary>
        /// 도구 삭제 메뉴 클릭
        /// </summary>
        private void DeleteToolMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                var contextMenu = menuItem.Parent as ContextMenu;
                var border = contextMenu?.PlacementTarget as Border;
                var tool = border?.DataContext as ToolItem;

                if (tool == null) return;

                var vm = DataContext as MainViewModel;
                vm?.RemoveToolCommand.Execute(tool);
            }
        }

        /// <summary>
        /// 도구 이름 변경 메뉴 클릭
        /// </summary>
        private void RenameToolMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                var contextMenu = menuItem.Parent as ContextMenu;
                var border = contextMenu?.PlacementTarget as Border;
                var tool = border?.DataContext as ToolItem;

                if (tool == null) return;

                var dialog = new RenameDialog
                {
                    Owner = this,
                    ToolName = tool.Name
                };

                if (dialog.ShowDialog() == true)
                {
                    tool.Name = dialog.ToolName;
                    if (tool.VisionTool != null)
                    {
                        tool.VisionTool.Name = dialog.ToolName;
                    }
                }
            }
        }

        /// <summary>
        /// 연결 모드에서 마우스 이동 - 임시 연결선 그리기
        /// </summary>
        private void ConnectionMode_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isConnectionMode || _connectionSourceTool == null) return;

            TempConnectionCanvas.Children.Clear();

            const double toolWidth = 150;
            const double toolHeight = 50;

            double sourceX = _connectionSourceTool.X + toolWidth / 2;
            double sourceY = _connectionSourceTool.Y + toolHeight / 2;

            // WorkspaceArea가 Grid 안에 있으므로, Grid 기준 좌표로 변환
            var grid = WorkspaceArea.Parent as Grid;
            if (grid == null) return;

            System.Windows.Point mousePos = e.GetPosition(grid);

            var brush = GetConnectionBrush(_pendingConnectionType);

            var tempLine = new Line
            {
                X1 = sourceX,
                Y1 = sourceY,
                X2 = mousePos.X,
                Y2 = mousePos.Y,
                Stroke = brush,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                Opacity = 0.7,
                IsHitTestVisible = false
            };
            TempConnectionCanvas.Children.Add(tempLine);
        }

        /// <summary>
        /// 연결 모드에서 ESC 키 누름 - 연결 모드 취소
        /// </summary>
        private void ConnectionMode_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelConnectionMode();
            }
        }

        /// <summary>
        /// 연결 모드 취소 및 정리
        /// </summary>
        private void CancelConnectionMode()
        {
            _isConnectionMode = false;
            _connectionSourceTool = null;
            _tempConnectionLine = null;

            TempConnectionCanvas.Children.Clear();
            ConnectionModeHint.Visibility = Visibility.Collapsed;

            this.KeyDown -= ConnectionMode_KeyDown;
            WorkspaceArea.MouseMove -= ConnectionMode_MouseMove;
        }

        /// <summary>
        /// 연결 모드에서 대상 도구 클릭 시 연결 완성
        /// </summary>
        private void TryCompleteConnection(ToolItem targetTool)
        {
            if (!_isConnectionMode || _connectionSourceTool == null) return;

            // 자기 자신에게 연결 방지
            if (_connectionSourceTool.Id == targetTool.Id)
            {
                CancelConnectionMode();
                return;
            }

            var vm = DataContext as MainViewModel;
            if (vm != null)
            {
                vm.AddConnection(_connectionSourceTool, targetTool, _pendingConnectionType);
            }

            CancelConnectionMode();
        }

        #endregion


        #region ImageCanvas ROI Events

        /// <summary>
        /// ROI 생성 이벤트 처리
        /// </summary>
        private void ImageCanvas_ROICreated(object? sender, ROIShape roi)
        {
            var vm = DataContext as MainViewModel;
            if (vm == null) return;

            if (_isDrawingSearchRegion)
            {
                _isDrawingSearchRegion = false;
                vm.OnSearchRegionCreated(roi);
            }
            else
            {
                vm.OnROICreated(roi);
            }
        }

        /// <summary>
        /// ROI 수정 이벤트 처리
        /// </summary>
        private void ImageCanvas_ROIModified(object? sender, ROIShape roi)
        {
            var vm = DataContext as MainViewModel;
            vm?.OnROIModified(roi);
        }

        /// <summary>
        /// ROI 선택 변경 이벤트 처리
        /// </summary>
        private void ImageCanvas_ROISelectionChanged(object? sender, ROIShape? roi)
        {
            var vm = DataContext as MainViewModel;
            vm?.OnROISelectionChanged(roi);
        }

        #endregion

        /// <summary>
        /// 도구 드롭 처리
        /// </summary>
        private void Sidebar2_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Object"))
            {
                var sourceTool = e.Data.GetData("Object") as ToolItem;
                var vm = DataContext as MainViewModel;
                var dropContainer = sender as UIElement;

                if (vm != null && sourceTool != null && dropContainer != null)
                {
                    System.Windows.Point position = e.GetPosition(dropContainer);

                    // ViewModel의 CreateDroppedTool 메서드를 사용하여 새 도구 생성
                    var newTool = vm.CreateDroppedTool(sourceTool, position.X, position.Y);

                    if (newTool != null)
                    {
                        // 새로 생성된 도구를 선택
                        vm.SelectedTool = newTool;
                    }
                }
            }
        }

        /// <summary>
        /// 도구 팔레트에서 드래그 시작
        /// </summary>
        private void ToolItem_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is FrameworkElement fe && fe.DataContext is ToolItem tool)
            {
                DataObject data = new DataObject();
                data.SetData("Object", tool);
                DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
            }
        }

        /// <summary>
        /// 워크스페이스 아이템 클릭 (드래그 시작 또는 연결 대상 선택)
        /// </summary>
        private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            // 연결 모드인 경우: 대상 도구로 연결 완성
            if (_isConnectionMode && element.DataContext is ToolItem targetTool)
            {
                TryCompleteConnection(targetTool);
                e.Handled = true;
                return;
            }

            // 일반 모드: 드래그 시작
            _isDragging = true;
            _mouseOffset = e.GetPosition(element);
            element.CaptureMouse();

            // 클릭한 도구를 선택
            if (element.DataContext is ToolItem tool)
            {
                var vm = DataContext as MainViewModel;
                if (vm != null)
                {
                    // 기존 선택 해제
                    foreach (var t in vm.DroppedTools)
                        t.IsSelected = false;

                    // 현재 도구 선택
                    tool.IsSelected = true;
                    vm.SelectedTool = tool;
                }
            }
        }

        /// <summary>
        /// 워크스페이스 아이템 드래그 이동
        /// </summary>
        private void Item_MouseMove(object sender, MouseEventArgs e)
        {
            var element = sender as FrameworkElement;

            if (_isDragging && element != null && element.DataContext is ToolItem tool)
            {
                var canvas = FindParent<Canvas>(element);
                if (canvas == null) return;

                System.Windows.Point currentPoint = e.GetPosition(canvas);
                tool.X = currentPoint.X - _mouseOffset.X;
                tool.Y = currentPoint.Y - _mouseOffset.Y;
            }
        }

        /// <summary>
        /// 워크스페이스 아이템 드래그 종료
        /// </summary>
        private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element != null)
            {
                _isDragging = false;
                element.ReleaseMouseCapture();
            }
        }

        /// <summary>
        /// 워크스페이스 아이템 우클릭 (선택)
        /// </summary>
        private void Item_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 연결 모드에서 우클릭하면 취소
            if (_isConnectionMode)
            {
                CancelConnectionMode();
                e.Handled = true;
                return;
            }

            var element = sender as FrameworkElement;
            if (element?.DataContext is ToolItem tool)
            {
                var vm = DataContext as MainViewModel;
                if (vm != null)
                {
                    // 기존 선택 해제
                    foreach (var t in vm.DroppedTools)
                        t.IsSelected = false;

                    // 현재 도구 선택
                    tool.IsSelected = true;
                    vm.SelectedTool = tool;
                }
            }
        }

        /// <summary>
        /// 부모 컨트롤 찾기 헬퍼 메서드
        /// </summary>
        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject? parentObject = VisualTreeHelper.GetParent(child);
            if (parentObject == null) return null;
            if (parentObject is T parent) return parent;
            return FindParent<T>(parentObject);
        }

        #region Recipe & Camera Management

        /// <summary>
        /// 레시피 관리자 열기
        /// </summary>
        private void RecipeManager_Click(object sender, RoutedEventArgs e)
        {
            var window = new RecipeManagerWindow();
            window.Owner = this;
            window.RecipeLoaded += RecipeManagerWindow_RecipeLoaded;
            window.ShowDialog();
        }

        /// <summary>
        /// 레시피 로드 이벤트 처리
        /// </summary>
        private void RecipeManagerWindow_RecipeLoaded(object? sender, Models.Recipe recipe)
        {
            var vm = DataContext as MainViewModel;
            if (vm != null)
            {
                vm.CurrentRecipeName = recipe.Name;
                vm.StatusMessage = $"Recipe loaded: {recipe.Name}";
            }

            // 창 닫기
            if (sender is RecipeManagerWindow window)
            {
                window.DialogResult = true;
            }
        }

        /// <summary>
        /// 카메라 관리자 열기
        /// </summary>
        private void CameraManager_Click(object sender, RoutedEventArgs e)
        {
            var window = new CameraManagerWindow();
            window.Owner = this;
            window.ShowDialog();

            // 카메라 목록 갱신
            var vm = DataContext as MainViewModel;
            vm?.RefreshCamerasFromService();
        }

        /// <summary>
        /// 현재 레시피 저장
        /// </summary>
        private void SaveRecipe_Click(object sender, RoutedEventArgs e)
        {
            var currentRecipe = RecipeService.Instance.CurrentRecipe;
            if (currentRecipe != null)
            {
                // 선택된 스텝이 있으면 워크스페이스의 도구를 스텝에 먼저 저장
                var vm = DataContext as MainViewModel;
                vm?.SaveWorkspaceToStep();

                currentRecipe.ModifiedAt = DateTime.Now;
                RecipeService.Instance.SaveRecipe(currentRecipe);

                if (vm != null)
                {
                    vm.StatusMessage = $"Recipe saved: {currentRecipe.Name}";
                }
            }
            else
            {
                MessageBox.Show("저장할 레시피가 없습니다. Recipe Manager에서 레시피를 로드하세요.",
                    "No Recipe", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion
    }
}
