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

        public IRelayCommand AddPlcMappingCommand => new RelayCommand(() =>
        {
            var keys = AvailableResultKeys;
            PlcMappings.Add(new PlcResultMapping
            {
                ResultKey = keys.Count > 0 ? keys[0] : "Success"
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

        // View-state: ResultTool overrides to true (최종 판정은 시퀀스 에디터가 담당)
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
                PopulateFromCache(items);
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

                var items = SyncService.GetAll();
                if (items.Count > 0)
                {
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
                        PopulateFromCache(items));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ToolSettings] LoadParamCodesAsync failed: {ex.Message}");
            }
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
            if (item == null || item.ParamCode == null)
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
        }
    }
}
