using VMS.VisionSetup.Controls;
using VMS.VisionSetup.Helpers;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.ViewModels;
using VMS.VisionSetup.Views;
using VMS.VisionSetup.VisionTools.PatternMatching;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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

        public MainView()
        {
            InitializeComponent();

            // DataContext is set by App.xaml.cs via DI
            DataContextChanged += OnDataContextChanged;

            // 창 닫힘 = 앱 종료 경로 — 카메라 SDK 스레드/공유 메모리/수신 루프를 정리해
            // 프로세스 잔존 방지 (App.xaml ShutdownMode=OnMainWindowClose 와 한 쌍).
            Closing += (_, _) => (DataContext as MainViewModel)?.Cleanup();

            // WeldTeach(로봇 티칭 PoC) 실행 메뉴 — exe 가 있는 PC 에서만 나타난다.
            // 배포 MSI 에는 WeldTeach 가 없으므로 GS 인증·현장 설치본에서는 메뉴가 보이지
            // 않고, 개발·테스트 PC 에서는 Release 실행본에서도 쓸 수 있다 (런타임 게이트 —
            // 자세한 사유는 MainViewModel.FindWeldTeachExe 주석).
            // 시작 시 1회 판정: 실행 중 WeldTeach 를 새로 빌드했다면 재시작해야 메뉴가 뜬다.
            if (MainViewModel.FindWeldTeachExe() != null)
            {
                var weldTeachItem = new System.Windows.Controls.MenuItem { Header = "_WeldTeach" };
                weldTeachItem.Click += (_, _) => (DataContext as MainViewModel)?.LaunchWeldTeach();
                MainMenu.Items.Add(weldTeachItem);
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not MainViewModel vm) return;

            // If recipe was pre-loaded via command-line argument, update UI
            var preloadedRecipe = vm.GetCurrentRecipe();
            if (preloadedRecipe != null)
            {
                vm.CurrentRecipeName = preloadedRecipe.Name;
                vm.StatusMessage = $"Recipe loaded: {preloadedRecipe.Name}";
            }

            // 연결 컬렉션 변경 감지
            vm.Connections.CollectionChanged += Connections_CollectionChanged;

            // 도구 위치 변경 시 연결선 업데이트를 위한 이벤트 등록
            vm.DroppedTools.CollectionChanged += DroppedTools_CollectionChanged;

            // ROI 동기화 이벤트 구독 via WeakReferenceMessenger
            WeakReferenceMessenger.Default.Register<RequestShowToolROIMessage>(this, (r, m) =>
            {
                // Tool 전환 등으로 ROI 가 null 이면 SearchRegion 도 함께 clear (일관성).
                if (m.ROIShape == null)
                {
                    ImageCanvasControl.ShowToolROIs(null, null);
                    return;
                }
                var ft = vm.SelectedVisionTool as FeatureMatchTool;
                var searchRoi = (ft != null && ft.UseSearchRegion) ? ft.AssociatedSearchRegionShape : null;
                ImageCanvasControl.ShowToolROIs(m.ROIShape, searchRoi);
            });
            WeakReferenceMessenger.Default.Register<RequestRefreshROIMessage>(this, (r, m) =>
            {
                ImageCanvasControl.RefreshROIVisual(m.ROIShape);
            });

            // View-level messages via WeakReferenceMessenger
            WeakReferenceMessenger.Default.Register<RequestDrawROIMessage>(this, (r, msg) =>
            {
                _isDrawingSearchRegion = false;
                EditMode mode;
                if (msg.UseAnnulus)
                    mode = EditMode.DrawAnnulus;
                else if (msg.UseCircle)
                    mode = EditMode.DrawCircle;
                else if (msg.UseAffine)
                    mode = EditMode.DrawRectangleAffine;
                else
                    mode = EditMode.DrawRectangle;
                ImageCanvasControl.ActivateDrawingMode(mode);
            });
            WeakReferenceMessenger.Default.Register<RequestClearROIMessage>(this, (r, msg) =>
            {
                _isDrawingSearchRegion = false;
                var ft = vm.SelectedVisionTool as FeatureMatchTool;
                ImageCanvasControl.ShowToolROIs(null, ft?.AssociatedSearchRegionShape);
            });
            WeakReferenceMessenger.Default.Register<RequestDrawSearchRegionMessage>(this, (r, msg) =>
            {
                _isDrawingSearchRegion = true;
                ImageCanvasControl.ActivateDrawingMode(EditMode.DrawRectangle);
            });
            WeakReferenceMessenger.Default.Register<RequestClearSearchRegionMessage>(this, (r, msg) =>
            {
                _isDrawingSearchRegion = false;
                vm.ClearSearchRegion();
            });

            // 이미지 한 픽셀 픽 (ColorMatchTool)
            WeakReferenceMessenger.Default.Register<RequestPickColorMessage>(this, (r, msg) =>
            {
                ImageCanvasControl.ActivateDrawingMode(EditMode.PickPoint);
            });
            ImageCanvasControl.PointPicked += (imgX, imgY) =>
            {
                if (DataContext is MainViewModel mvm)
                    mvm.OnColorPickedFromImage(imgX, imgY);
            };
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
        /// 단일 연결선 그리기 (베지어 곡선 + 장애물 우회)
        /// </summary>
        private void DrawConnectionLine(ToolConnection connection)
        {
            if (connection.SourceToolItem == null || connection.TargetToolItem == null)
                return;

            var vm = DataContext as MainViewModel;
            if (vm == null) return;

            var brush = GetConnectionBrush(connection.Type);

            // ConnectionLineRouter를 사용하여 베지어 경로 계산
            var result = ConnectionLineRouter.ComputePath(
                connection.SourceToolItem,
                connection.TargetToolItem,
                vm.DroppedTools);

            // 베지어 경로 그리기
            var path = new Path
            {
                Data = result.PathGeometry,
                Stroke = brush,
                StrokeThickness = 2,
                StrokeDashArray = GetDashArray(connection.Type),
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            ConnectionCanvas.Children.Add(path);

            // 화살표 머리 그리기 (곡선 접선 방향 기반)
            DrawArrowHead(result.ArrowTipPoint, result.ArrowAngle, brush);

            // 연결 타입 라벨 (곡선 중간점에 배치)
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
            Canvas.SetLeft(label, result.LabelPosition.X - 20);
            Canvas.SetTop(label, result.LabelPosition.Y - 10);
            ConnectionCanvas.Children.Add(label);
        }

        /// <summary>
        /// 화살표 머리 그리기 (곡선 접선 각도 기반)
        /// </summary>
        private void DrawArrowHead(Point tipPoint, double angle, Brush brush)
        {
            double arrowLength = 12;
            double arrowWidth = Math.PI / 6; // 30도

            var point1 = new Point(
                tipPoint.X - arrowLength * Math.Cos(angle - arrowWidth),
                tipPoint.Y - arrowLength * Math.Sin(angle - arrowWidth));

            var point2 = new Point(
                tipPoint.X - arrowLength * Math.Cos(angle + arrowWidth),
                tipPoint.Y - arrowLength * Math.Sin(angle + arrowWidth));

            var polygon = new Polygon
            {
                Points = new PointCollection { tipPoint, point1, point2 },
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

                var vm = DataContext as MainViewModel;
                vm?.RenameTool(tool);
            }
        }

        /// <summary>
        /// 연결 모드에서 마우스 이동 - 임시 베지어 연결선 그리기
        /// </summary>
        private void ConnectionMode_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isConnectionMode || _connectionSourceTool == null) return;

            TempConnectionCanvas.Children.Clear();

            const double toolWidth = 150;
            const double toolHeight = 50;

            double sourceX = _connectionSourceTool.X + toolWidth / 2;
            double sourceY = _connectionSourceTool.Y + toolHeight / 2;
            var sourceCenter = new Point(sourceX, sourceY);

            // WorkspaceArea가 Grid 안에 있으므로, Grid 기준 좌표로 변환
            var grid = WorkspaceArea.Parent as Grid;
            if (grid == null) return;

            Point mousePos = e.GetPosition(grid);

            var brush = GetConnectionBrush(_pendingConnectionType);

            // 베지어 곡선 임시 경로
            var tempGeometry = ConnectionLineRouter.ComputeTempPath(sourceCenter, mousePos);
            var tempPath = new Path
            {
                Data = tempGeometry,
                Stroke = brush,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 4 },
                Opacity = 0.7,
                Fill = Brushes.Transparent,
                IsHitTestVisible = false
            };
            TempConnectionCanvas.Children.Add(tempPath);
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
                // HelpIcon(또는 그 자식) 위에서 시작된 마우스 이벤트면 드래그 시작 안 함.
                // 그렇지 않으면 ? 클릭이 드래그로 인식되어 ToggleButton.Click이 안 발생.
                if (IsOriginatingFrom<VMS.VisionSetup.Controls.HelpIcon>(e.OriginalSource)) return;

                DataObject data = new DataObject();
                data.SetData("Object", tool);
                DragDrop.DoDragDrop(fe, data, DragDropEffects.Copy);
            }
        }

        private static bool IsOriginatingFrom<T>(object? source) where T : DependencyObject
        {
            var current = source as DependencyObject;
            while (current != null)
            {
                if (current is T) return true;
                current = current is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
                    ? System.Windows.Media.VisualTreeHelper.GetParent(current)
                    : System.Windows.LogicalTreeHelper.GetParent(current);
            }
            return false;
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

        /// <summary>비주얼 트리에서 첫 번째 자식 컨트롤 찾기 (DataGrid 내부 ScrollViewer 등).</summary>
        private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var deeper = FindChild<T>(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        #region Run Results DataGrid (행 펼침 토글 · 휠 스크롤)

        /// <summary>
        /// 이미 펼쳐진(선택된) 행을 다시 클릭하면 닫기 — 선택 해제로 RowDetails 접힘.
        /// 펼침 영역(RowDetails) 내부 클릭은 무시해 상세 스크롤 조작을 방해하지 않는다.
        /// </summary>
        private void ResultRow_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGridRow row || !row.IsSelected) return;
            if (e.OriginalSource is DependencyObject src
                && FindParent<System.Windows.Controls.Primitives.DataGridDetailsPresenter>(src) != null)
                return;

            RunResultsGrid.SelectedItem = null;
            e.Handled = true;
        }

        /// <summary>
        /// DataGrid 위 마우스 휠 스크롤 — 행 펼침(RowDetails) 위에서는 휠 이벤트가
        /// 내부 요소에 먹혀 그리드가 스크롤되지 않는 문제 보정.
        /// 상세 영역 내부 ScrollViewer가 더 스크롤할 수 있으면 그쪽을 우선한다.
        /// </summary>
        private void RunResultsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DataGrid grid) return;

            var mainScroll = FindChild<ScrollViewer>(grid);
            if (mainScroll == null) return;

            // 상세 영역 안의 내부 ScrollViewer가 해당 방향으로 스크롤 여지가 있으면 기본 동작 유지
            if (e.OriginalSource is DependencyObject src)
            {
                var innerScroll = FindParent<ScrollViewer>(src);
                if (innerScroll != null && !ReferenceEquals(innerScroll, mainScroll))
                {
                    bool innerCanScroll = e.Delta < 0
                        ? innerScroll.VerticalOffset < innerScroll.ScrollableHeight
                        : innerScroll.VerticalOffset > 0;
                    if (innerCanScroll) return;
                }
            }

            mainScroll.ScrollToVerticalOffset(mainScroll.VerticalOffset - e.Delta / 40.0);
            e.Handled = true;
        }

        #endregion

        #region Recipe & Camera Management

        /// <summary>
        /// 레시피 관리자 열기
        /// </summary>
        private void RecipeManager_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            vm?.OpenRecipeManager();
        }

        /// <summary>
        /// 카메라 관리자 열기
        /// </summary>
        private void CameraManager_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            vm?.OpenCameraManager();
        }

        /// <summary>
        /// 캘리브레이션 관리자 열기
        /// </summary>
        private void CalibrationManager_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            vm?.OpenCalibrationManager();
        }

        /// <summary>
        /// 시퀀스 에디터 열기
        /// </summary>
        private void SequenceEditor_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            vm?.OpenSequenceEditor();
        }

        /// <summary>
        /// Deep Learning 라벨링/학습 앱 실행
        /// </summary>
        private void DeepLearning_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MainViewModel;

            vm?.LaunchDeepLearning();
        }

        /// <summary>
        /// ONNX Runtime 설정 다이얼로그 — Execution Provider / TensorRT 캐시·FP16 편집.
        /// </summary>
        private void OnnxSettings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OnnxSettingsDialog { Owner = System.Windows.Window.GetWindow(this) };
            dialog.ShowDialog();
        }

        /// <summary>
        /// 배치 테스트 러너 — 폴더 이미지에 현재 레시피 일괄 적용 → CSV 리포트.
        /// </summary>
        private void BatchTest_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VMS.VisionSetup.Views.BatchTest.BatchTestWindow(
                VMS.VisionSetup.Services.VisionService.Instance,
                VMS.VisionSetup.Services.RecipeService.Instance,
                VMS.VisionSetup.Services.CameraService.Instance)
            {
                Owner = System.Windows.Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }

        /// <summary>
        /// 합성 OCR 데이터 생성기 윈도우 — PP-OCR fine-tuning / OCV 학습용 데이터셋 자동 생성.
        /// </summary>
        private void SynthData_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new VMS.VisionSetup.Views.SynthData.SynthDataWindow
            {
                Owner = System.Windows.Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }

        /// <summary>
        /// MultiView 스캔 설정 다이얼로그 열기
        /// </summary>
        private void MultiViewSetup_Click(object sender, RoutedEventArgs e)
        {
            var window = new MultiViewSetupWindow
            {
                Owner = this,
                DataContext = this.DataContext  // MainViewModel 공유
            };
            window.ShowDialog();
        }

        private void ToolPaletteExpandAll_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                foreach (var category in vm.ToolTree)
                    category.IsExpanded = true;
            }
        }

        private void ToolPaletteCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                foreach (var category in vm.ToolTree)
                    category.IsExpanded = false;
            }
        }

        #endregion
    }
}
