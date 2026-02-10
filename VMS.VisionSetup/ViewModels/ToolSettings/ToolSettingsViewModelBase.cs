using VMS.VisionSetup.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenCvSharp;
using System;
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

        // Commands (owned by VM, send messages)
        public IRelayCommand DrawROICommand { get; }
        public IRelayCommand ClearROICommand { get; }

        // View-state: FeatureMatchTool overrides to true (has its own ROI section)
        public virtual bool HasCustomROISection => false;

        public virtual void Dispose()
        {
            Tool.PropertyChanged -= OnModelPropertyChanged;
        }
    }
}
