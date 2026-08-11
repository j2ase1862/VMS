using VMS.VisionSetup.Models;
using VMS.VisionSetup.VisionTools.BlobAnalysis;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.Core.Interfaces;
using VMS.PLC.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    public abstract class ToolSettingsViewModelBase : ObservableObject, IDisposable
    {
        public VisionToolBase Tool { get; }

        protected ToolSettingsViewModelBase(VisionToolBase tool)
        {
            Tool = tool;
            tool.PropertyChanged += OnModelPropertyChanged;

            // Expert 모드 — 같은 ToolType 다른 인스턴스에서 토글해도 즉시 반영되도록 이벤트 구독.
            Services.ExpertModeService.Instance.ExpertModeChanged += OnExpertModeChanged;

            // Web 파라미터 캐시 갱신 → ParamCode 콤보 재구성 (Messenger 는 약한 참조라
            // 툴 재선택으로 VM 이 교체되어도 누수 없음. Dispose 에서 명시 해제도 수행.)
            WeakReferenceMessenger.Default.Register<WebParamCacheUpdatedMessage>(this,
                static (recipient, _) => ((ToolSettingsViewModelBase)recipient).RefreshParamCodesFromCache());

            DrawROICommand = new RelayCommand(() =>
            {
                if (Tool is CircleFitTool)
                {
                    WeakReferenceMessenger.Default.Send(new RequestDrawROIMessage(useAffine: false, useCircle: true));
                    return;
                }
                bool isMeasurement = Tool is LineFitTool or CaliperTool or BlobTool;
                WeakReferenceMessenger.Default.Send(new RequestDrawROIMessage(isMeasurement));
            });

            ClearROICommand = new RelayCommand(() =>
            {
                UseROI = false;
                ROI = new Rect();
                AssociatedROIShape = null;
                WeakReferenceMessenger.Default.Send(new RequestClearROIMessage());
            });

            ShowROICommand = new RelayCommand(() =>
            {
                if (!UseROI || ROIWidth <= 0 || ROIHeight <= 0) return;

                bool isAffineTool = Tool is LineFitTool or CaliperTool or BlobTool;
                bool isCircleFitTool = Tool is CircleFitTool;

                // Affine 도구인데 기존 ROI가 RectangleROI면 → RectangleAffineROI로 변환
                if (isAffineTool && AssociatedROIShape is RectangleROI oldRect)
                {
                    AssociatedROIShape = new RectangleAffineROI(
                        oldRect.X + oldRect.Width / 2.0,
                        oldRect.Y + oldRect.Height / 2.0,
                        oldRect.Width,
                        oldRect.Height,
                        Tool.ROIAngle)
                    {
                        Name = oldRect.Name,
                        ShowSearchArrow = Tool is LineFitTool or CaliperTool
                    };
                }

                // CircleFitTool인데 기존 ROI가 CircleROI가 아니면 → CircleROI로 변환
                if (isCircleFitTool && AssociatedROIShape is not CircleROI)
                {
                    var cft = (CircleFitTool)Tool;
                    AssociatedROIShape = new CircleROI(
                        cft.CenterPoint.X, cft.CenterPoint.Y, cft.ExpectedRadius)
                    {
                        Name = $"{Name} ROI",
                        ShowSearchArrow = true,
                        SearchOutward = cft.SearchDirection == CircleSearchDirection.InwardToOutward
                    };
                }

                // CircleROI의 검색 방향 동기화
                if (isCircleFitTool && AssociatedROIShape is CircleROI existingCircle)
                {
                    var cft = (CircleFitTool)Tool;
                    existingCircle.ShowSearchArrow = true;
                    existingCircle.SearchOutward = cft.SearchDirection == CircleSearchDirection.InwardToOutward;
                }

                if (AssociatedROIShape == null)
                {
                    if (isCircleFitTool)
                    {
                        var cft = (CircleFitTool)Tool;
                        AssociatedROIShape = new CircleROI(
                            cft.CenterPoint.X, cft.CenterPoint.Y, cft.ExpectedRadius)
                        {
                            Name = $"{Name} ROI",
                            ShowSearchArrow = true,
                            SearchOutward = cft.SearchDirection == CircleSearchDirection.InwardToOutward
                        };
                    }
                    else if (isAffineTool)
                    {
                        AssociatedROIShape = new RectangleAffineROI(
                            Tool.ROICenterX != 0 ? Tool.ROICenterX : ROIX + ROIWidth / 2.0,
                            Tool.ROICenterY != 0 ? Tool.ROICenterY : ROIY + ROIHeight / 2.0,
                            ROIWidth,
                            ROIHeight,
                            Tool.ROIAngle)
                        {
                            Name = $"{Name} ROI",
                            ShowSearchArrow = Tool is LineFitTool or CaliperTool
                        };
                    }
                    else
                    {
                        AssociatedROIShape = new RectangleROI
                        {
                            X = ROIX,
                            Y = ROIY,
                            Width = ROIWidth,
                            Height = ROIHeight,
                            Name = $"{Name} ROI",
                            ShowSearchArrow = false
                        };
                    }
                }

                WeakReferenceMessenger.Default.Send(new RequestShowToolROIMessage(AssociatedROIShape));
            });
        }

        private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
            => OnToolPropertyChanged(e.PropertyName);

        protected virtual void OnToolPropertyChanged(string? propertyName)
        {
            if (propertyName != null)
                OnPropertyChanged(propertyName);
        }

        // Common forwarded properties
        public string Name { get => Tool.Name; set => Tool.Name = value; }
        public string ToolType { get => Tool.ToolType; set => Tool.ToolType = value; }
        public bool IsEnabled { get => Tool.IsEnabled; set => Tool.IsEnabled = value; }
        public bool UseROI { get => Tool.UseROI; set => Tool.UseROI = value; }

        public Rect ROI { get => Tool.ROI; set => Tool.ROI = value; }
        public int ROIX { get => Tool.ROIX; set => Tool.ROIX = value; }
        public int ROIY { get => Tool.ROIY; set => Tool.ROIY = value; }
        public int ROIWidth { get => Tool.ROIWidth; set => Tool.ROIWidth = value; }
        public int ROIHeight { get => Tool.ROIHeight; set => Tool.ROIHeight = value; }

        public double ROIAngle { get => Tool.ROIAngle; set => Tool.ROIAngle = value; }

        public ROIShape? AssociatedROIShape { get => Tool.AssociatedROIShape; set => Tool.AssociatedROIShape = value; }

        public double ExecutionTime { get => Tool.ExecutionTime; set => Tool.ExecutionTime = value; }
        public VisionResult? LastResult { get => Tool.LastResult; set => Tool.LastResult = value; }

        // PLC result mappings (1:N)
        public ObservableCollection<PlcResultMapping> PlcMappings => Tool.PlcMappings;
        public List<string> AvailableResultKeys => Tool.GetAvailableResultKeys();
        public Array PlcDataTypes => Enum.GetValues(typeof(PlcDataType));

        /// <summary>
        /// PLC + IO 보드 디바이스 목록 — Tool Output 매핑 DataGrid 의 Device 콤보 ItemsSource.
        /// Phase A 에서 SequenceEditorContext.ExtraDevices 에 host(VMS) 또는 standalone(VMS.VisionSetup)
        /// 가 채워둠 — 비어 있으면 기본 MainPLC entry 안전망 제공.
        /// </summary>
        public IReadOnlyList<Services.SequenceDeviceEntry> AvailableDevices
        {
            get
            {
                var src = Services.SequenceEditorContext.ExtraDevices;
                if (src.Count > 0) return src;
                return new[] { new Services.SequenceDeviceEntry("MainPLC", PLC.Models.IoDeviceType.Plc) };
            }
        }

        public IRelayCommand AddPlcMappingCommand => new RelayCommand(() =>
        {
            var keys = AvailableResultKeys;
            PlcMappings.Add(new PlcResultMapping
            {
                ResultKey = keys.Count > 0 ? keys[0] : "Success",
                DeviceId = AvailableDevices.Count > 0 ? AvailableDevices[0].DeviceId : "MainPLC"
            });
        });

        public IRelayCommand<PlcResultMapping> RemovePlcMappingCommand => new RelayCommand<PlcResultMapping>(mapping =>
        {
            if (mapping != null)
                PlcMappings.Remove(mapping);
        });

        // Commands (owned by VM, send messages)
        public IRelayCommand ShowROICommand { get; }
        public IRelayCommand DrawROICommand { get; }
        public IRelayCommand ClearROICommand { get; }

        // View-state: FeatureMatchTool overrides to true (has its own ROI section)
        public virtual bool HasCustomROISection => false;

        // View-state: PLC Output 섹션 숨김용. 현재 override 없음 — ResultTool 도
        // 통계 키(PassCount 등) 전송을 위해 노출한다 (최종 OK/NG 는 시퀀스 에디터 담당).
        public virtual bool HidePlcSection => false;

        // ── Web Parameter Link (ParamCode) ──

        /// <summary>ParameterSyncService 참조 (App 시작 시 정적 설정)</summary>
        public static IParameterSyncService? SyncService { get; set; }

        /// <summary>ComboBox ItemsSource용 — "None" + 동기화된 파라미터 목록</summary>
        public ObservableCollection<ParamCodeItem> AvailableParamCodes { get; } = new();

        /// <summary>AvailableParamCodes 목록을 SyncService 캐시에서 로드.
        /// 캐시가 비어있으면 비동기로 첫 번째 레시피를 자동 로드 시도.</summary>
        public void LoadAvailableParamCodes()
        {
            if (SyncService == null)
            {
                EnsureNoneItem();
                return;
            }

            var items = SyncService.GetAll();
            if (items.Count > 0)
            {
                // 즉시 캐시로 채우고, 백그라운드로 최신화 — Web에서 방금 추가한
                // 파라미터가 60초 주기를 기다리지 않고 곧바로 콤보에 반영되도록.
                PopulateFromCache(items);
                _ = LoadParamCodesAsync();
            }
            else
            {
                // 캐시가 비어있음 → 백그라운드에서 로드 후 UI 갱신
                EnsureNoneItem();
                _ = LoadParamCodesAsync();
            }
        }

        private async System.Threading.Tasks.Task LoadParamCodesAsync()
        {
            if (SyncService == null) return;

            try
            {
                // 레시피 목록이 없으면 먼저 가져오기
                if (SyncService.Recipes.Count == 0)
                    await SyncService.SyncRecipesAsync();

                // 현재 로드된 레시피가 없으면 첫 번째 레시피 로드
                if (SyncService.CurrentRecipeId <= 0 && SyncService.Recipes.Count > 0)
                    await SyncService.LoadRecipeAsync(SyncService.Recipes[0].Id);
                else if (SyncService.CurrentRecipeId > 0)
                    // 설정을 여는 시점에 캐시 강제 갱신 — Web에서 방금 추가한 파라미터가
                    // 60초 주기를 기다리지 않고 바로 콤보에 나타나도록
                    await SyncService.SyncAsync();

                var items = SyncService.GetAll();
                if (items.Count > 0)
                    ApplyParamCodes(items);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ToolSettings] LoadParamCodesAsync failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Web 파라미터 캐시 변경(WebParamCacheUpdatedMessage) 시 콤보 재구성.
        /// 재구성 후 전체 프로퍼티 변경 통지로 파생 VM 의 Selected*Code 선택을 복원한다.
        /// </summary>
        private void RefreshParamCodesFromCache()
        {
            if (SyncService == null) return;
            ApplyParamCodes(SyncService.GetAll());
        }

        /// <summary>
        /// 콤보 재구성 + 전체 프로퍼티 통지 — 재구성 중 WPF 가 밀어넣는 SelectedItem=null 은
        /// SetLinkedParamCode 가 무시하므로 기존 링크가 보존되고, 통지로 선택이 복원된다.
        /// </summary>
        private void ApplyParamCodes(List<Core.Models.ParameterSync.RecipeParameterDto> items)
        {
            void Apply()
            {
                PopulateFromCache(items);
                OnPropertyChanged(string.Empty);
            }

            // 헤드리스(테스트) 환경엔 Application 이 없다 — 직접 반영
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) Apply();
            else dispatcher.BeginInvoke(Apply);
        }

        private void PopulateFromCache(List<Core.Models.ParameterSync.RecipeParameterDto> items)
        {
            AvailableParamCodes.Clear();
            AvailableParamCodes.Add(new ParamCodeItem
            {
                ParamCode = null,
                DisplayText = "(None)"
            });

            foreach (var p in items)
            {
                var desc = string.IsNullOrEmpty(p.Description) ? "" : $" - {p.Description}";
                var unit = string.IsNullOrEmpty(p.Unit) ? "" : $" [{p.Unit}]";
                AvailableParamCodes.Add(new ParamCodeItem
                {
                    ParamCode = p.ParamCode,
                    DisplayText = $"#{p.ParamCode}: {p.ParamValue:F4}{unit}{desc}",
                    Value = p.ParamValue,
                    Unit = p.Unit
                });
            }
        }

        private void EnsureNoneItem()
        {
            if (AvailableParamCodes.Count == 0)
            {
                AvailableParamCodes.Add(new ParamCodeItem
                {
                    ParamCode = null,
                    DisplayText = "(None)"
                });
            }
        }

        /// <summary>
        /// 특정 프로퍼티에 연결된 ParamCodeItem을 반환 (ComboBox SelectedItem용).
        /// LinkedParamCodes에 없으면 "(None)" 항목 반환.
        /// </summary>
        public ParamCodeItem? GetLinkedParamCodeItem(string propertyName)
        {
            if (Tool.LinkedParamCodes.TryGetValue(propertyName, out var code))
                return AvailableParamCodes.FirstOrDefault(p => p.ParamCode == code);
            return AvailableParamCodes.FirstOrDefault(p => p.ParamCode == null);
        }

        /// <summary>
        /// ParamCode 선택 시 호출 — LinkedParamCodes 갱신 + 값 자동 적용.
        /// </summary>
        public void SetLinkedParamCode(string propertyName, ParamCodeItem? item)
        {
            // item == null 은 사용자 조작이 아니라 콤보 ItemsSource 재구성(Clear) 때
            // WPF 가 SelectedItem=null 을 밀어넣는 노이즈 — 링크를 지우면 안 된다
            // (Web 파라미터 추가 → 콤보 갱신 → 기존 링크 소실 사고, 2026-08-11).
            // 명시적 해제는 "(None)" 항목(ParamCode == null 인 실제 항목) 선택으로만.
            if (item == null) return;

            if (item.ParamCode == null)
            {
                // 연동 해제
                Tool.LinkedParamCodes.Remove(propertyName);
                return;
            }

            Tool.LinkedParamCodes[propertyName] = item.ParamCode.Value;

            // 값 자동 적용 (리플렉션)
            var prop = Tool.GetType().GetProperty(propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                try
                {
                    object converted;
                    if (prop.PropertyType == typeof(double))
                        converted = item.Value;
                    else if (prop.PropertyType == typeof(int))
                        converted = (int)Math.Round(item.Value);
                    else if (prop.PropertyType == typeof(float))
                        converted = (float)item.Value;
                    else
                        converted = Convert.ChangeType(item.Value, prop.PropertyType);

                    prop.SetValue(Tool, converted);
                    OnPropertyChanged(propertyName);
                    Debug.WriteLine($"[ParamCode] {Tool.Name}.{propertyName} = {item.Value} (Code #{item.ParamCode})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ParamCode] Failed to set {propertyName}: {ex.Message}");
                }
            }
        }


        public virtual void Dispose()
        {
            Tool.PropertyChanged -= OnModelPropertyChanged;
            Services.ExpertModeService.Instance.ExpertModeChanged -= OnExpertModeChanged;
            WeakReferenceMessenger.Default.Unregister<WebParamCacheUpdatedMessage>(this);
        }

        // ────────────────────────────────────────────────────────────
        // Expert Mode (도구 타입별 토글) — Tool Settings 헤더의 체크박스가 여기에 바인딩.
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// 현재 도구 타입(Tool.ToolType)의 Expert 모드 ON/OFF.
        /// XAML 의 Expert 전용 파라미터들이 Visibility 바인딩으로 이 값을 참조.
        /// 토글 시 ExpertModeService 에 저장되어 같은 ToolType 의 모든 인스턴스에 즉시 적용.
        /// </summary>
        public bool IsExpertMode
        {
            get => Services.ExpertModeService.Instance.IsExpert(Tool.ToolType);
            set
            {
                Services.ExpertModeService.Instance.SetExpert(Tool.ToolType, value);
                // OnExpertModeChanged 가 OnPropertyChanged 를 호출하므로 여기서 직접 호출 불필요.
            }
        }

        private void OnExpertModeChanged(object? sender, string changedToolType)
        {
            // 자신과 같은 ToolType 의 변경만 반응 — 다른 도구의 토글이 내 UI 를 흔들지 않게.
            if (string.Equals(changedToolType, Tool.ToolType, System.StringComparison.Ordinal))
                OnPropertyChanged(nameof(IsExpertMode));
        }
    }
}
