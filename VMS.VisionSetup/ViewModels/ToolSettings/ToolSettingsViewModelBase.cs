using VMS.VisionSetup.Models;
using VMS.PLC.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

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
                WeakReferenceMessenger.Default.Send(new RequestDrawROIMessage());
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

                if (AssociatedROIShape == null)
                {
                    AssociatedROIShape = new RectangleROI
                    {
                        X = ROIX,
                        Y = ROIY,
                        Width = ROIWidth,
                        Height = ROIHeight,
                        Name = $"{Name} ROI"
                    };
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

        public virtual void Dispose()
        {
            Tool.PropertyChanged -= OnModelPropertyChanged;
        }
    }
}
