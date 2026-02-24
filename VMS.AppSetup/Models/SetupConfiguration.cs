using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using VMS.Camera.Models;
using VMS.PLC.Models;

namespace VMS.AppSetup.Models
{
    /// <summary>
    /// 머신 비전 시스템 설정 구성
    /// </summary>
    public class SetupConfiguration
    {
        // Page 2: Application Settings
        public string ApplicationName { get; set; } = "BODA Vision System";
        public string SystemIpAddress { get; set; } = "192.168.0.1";

        // Page 3: Camera Mode
        public CameraMode CameraMode { get; set; } = CameraMode.Virtual;
        public List<CameraConfiguration> Cameras { get; set; } = new();

        // Page 4: PLC Settings
        public PlcVendor PlcVendor { get; set; } = PlcVendor.None;
        public PlcCommunicationType CommunicationType { get; set; } = PlcCommunicationType.Ethernet;
        public string PlcIpAddress { get; set; } = "192.168.0.100";
        public int PlcPort { get; set; } = 502;

        // Metadata
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string Version { get; set; } = "1.0.0";
    }

    /// <summary>
    /// 카메라 설정
    /// </summary>
    public partial class CameraConfiguration : ObservableObject
    {
        [ObservableProperty]
        private bool _isEnabled = true;

        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _name = string.Empty;

        [ObservableProperty]
        private string _ipAddress = string.Empty;

        [ObservableProperty]
        private CameraManufacturer _manufacturer = CameraManufacturer.Other;

        [JsonIgnore]
        public string DisplayInfo => $"{Manufacturer} - {IpAddress}";
    }

    /// <summary>
    /// 카메라 모드
    /// </summary>
    public enum CameraMode
    {
        Live,   // 실제 연결된 카메라 표시
        Virtual // 가상 카메라 설정
    }

}
