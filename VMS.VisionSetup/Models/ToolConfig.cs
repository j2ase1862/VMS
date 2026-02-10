using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 도구 간 연결 설정 (직렬화용)
    /// </summary>
    public class ToolConnectionConfig
    {
        /// <summary>
        /// 소스 도구 ID
        /// </summary>
        public string SourceToolId { get; set; } = string.Empty;

        /// <summary>
        /// 연결 타입 ("Image", "Coordinates", "Result")
        /// </summary>
        public string ConnectionType { get; set; } = "Image";
    }

    /// <summary>
    /// 도구 설정 (직렬화용)
    /// JSON 파일에 저장되는 도구 설정 데이터
    /// </summary>
    public class ToolConfig : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        /// <summary>
        /// 도구 고유 ID (GUID)
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private string _toolType = string.Empty;
        /// <summary>
        /// 도구 타입 (예: "CaliperTool", "BlobTool", "FeatureMatchTool")
        /// </summary>
        public string ToolType
        {
            get => _toolType;
            set => SetProperty(ref _toolType, value);
        }

        private string _name = string.Empty;
        /// <summary>
        /// 사용자 정의 도구 이름
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private int _sequence;
        /// <summary>
        /// Step 내 실행 순서
        /// </summary>
        public int Sequence
        {
            get => _sequence;
            set => SetProperty(ref _sequence, value);
        }

        private bool _isEnabled = true;
        /// <summary>
        /// 도구 활성화 여부
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private double _x;
        /// <summary>
        /// 캔버스 X 위치
        /// </summary>
        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        private double _y;
        /// <summary>
        /// 캔버스 Y 위치
        /// </summary>
        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        #region ROI Settings

        private bool _useROI;
        /// <summary>
        /// ROI 사용 여부
        /// </summary>
        public bool UseROI
        {
            get => _useROI;
            set => SetProperty(ref _useROI, value);
        }

        private int _roiX;
        /// <summary>
        /// ROI X 좌표
        /// </summary>
        public int ROIX
        {
            get => _roiX;
            set => SetProperty(ref _roiX, value);
        }

        private int _roiY;
        /// <summary>
        /// ROI Y 좌표
        /// </summary>
        public int ROIY
        {
            get => _roiY;
            set => SetProperty(ref _roiY, value);
        }

        private int _roiWidth;
        /// <summary>
        /// ROI 너비
        /// </summary>
        public int ROIWidth
        {
            get => _roiWidth;
            set => SetProperty(ref _roiWidth, value);
        }

        private int _roiHeight;
        /// <summary>
        /// ROI 높이
        /// </summary>
        public int ROIHeight
        {
            get => _roiHeight;
            set => SetProperty(ref _roiHeight, value);
        }

        #endregion

        /// <summary>
        /// 도구별 파라미터 (JSON 딕셔너리)
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new();

        /// <summary>
        /// 다른 도구와의 연결 설정
        /// </summary>
        public List<ToolConnectionConfig> Connections { get; set; } = new();

        /// <summary>
        /// ROI Shape 타입 ("Rectangle", "RectangleAffine", "Circle", "Ellipse", "Polygon")
        /// </summary>
        public string? ROIShapeType { get; set; }

        /// <summary>
        /// ROI Shape 추가 데이터 (회전 각도, 반지름 등)
        /// </summary>
        public Dictionary<string, object>? ROIShapeData { get; set; }
    }
}
