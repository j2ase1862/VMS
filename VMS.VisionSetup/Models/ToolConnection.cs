using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 도구 간 연결선 타입
    /// </summary>
    public enum ConnectionType
    {
        /// <summary>
        /// 이미지 전달 - Source 도구의 결과 이미지를 Target 도구의 입력 이미지로 전달
        /// </summary>
        Image,

        /// <summary>
        /// 좌표 전달 - Source 도구의 좌표 데이터를 Target 도구에 전달
        /// </summary>
        Coordinates,

        /// <summary>
        /// 결과 전달 - Source 도구의 실행 결과(Success/Fail)를 Target 도구에 전달
        /// </summary>
        Result
    }

    /// <summary>
    /// 도구 간 연결 정보
    /// </summary>
    public class ToolConnection : ObservableObject
    {
        private string _id = Guid.NewGuid().ToString();
        /// <summary>
        /// 연결 고유 ID
        /// </summary>
        public string Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        private ToolItem? _sourceToolItem;
        /// <summary>
        /// 연결 시작 도구 (데이터를 보내는 쪽)
        /// </summary>
        public ToolItem? SourceToolItem
        {
            get => _sourceToolItem;
            set => SetProperty(ref _sourceToolItem, value);
        }

        private ToolItem? _targetToolItem;
        /// <summary>
        /// 연결 대상 도구 (데이터를 받는 쪽)
        /// </summary>
        public ToolItem? TargetToolItem
        {
            get => _targetToolItem;
            set => SetProperty(ref _targetToolItem, value);
        }

        private ConnectionType _connectionType;
        /// <summary>
        /// 연결 타입 (Image, Coordinates, Result)
        /// </summary>
        public ConnectionType Type
        {
            get => _connectionType;
            set => SetProperty(ref _connectionType, value);
        }

        /// <summary>
        /// 연결선 색상 (타입에 따라 결정)
        /// </summary>
        public string LineColor => Type switch
        {
            ConnectionType.Image => "#4CAF50",        // 녹색
            ConnectionType.Coordinates => "#2196F3",   // 파란색
            ConnectionType.Result => "#FF9800",        // 주황색
            _ => "#FFFFFF"
        };

        /// <summary>
        /// 연결 타입 표시 이름
        /// </summary>
        public string TypeDisplayName => Type switch
        {
            ConnectionType.Image => "Image",
            ConnectionType.Coordinates => "Coordinates",
            ConnectionType.Result => "Result",
            _ => "Unknown"
        };
    }
}
