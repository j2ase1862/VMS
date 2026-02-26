using CommunityToolkit.Mvvm.ComponentModel;
using VMS.PLC.Models.Sequence;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 시퀀스 노드 캔버스 표시용 Observable 래퍼 (ToolItem 패턴 동일)
    /// </summary>
    public class SequenceNodeItem : ObservableObject
    {
        public string Id { get; }

        private SequenceNodeType _nodeType;
        public SequenceNodeType NodeType
        {
            get => _nodeType;
            set
            {
                if (SetProperty(ref _nodeType, value))
                {
                    OnPropertyChanged(nameof(NodeColor));
                    OnPropertyChanged(nameof(NodeIcon));
                    OnPropertyChanged(nameof(HasConditionStatus));
                }
            }
        }

        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private double _x;
        public double X
        {
            get => _x;
            set => SetProperty(ref _x, value);
        }

        private double _y;
        public double Y
        {
            get => _y;
            set => SetProperty(ref _y, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set => SetProperty(ref _isActive, value);
        }

        /// <summary>PLC 모니터링 조건 충족 상태 (null=미평가)</summary>
        private bool? _conditionStatus;
        public bool? ConditionStatus
        {
            get => _conditionStatus;
            set
            {
                if (SetProperty(ref _conditionStatus, value))
                {
                    OnPropertyChanged(nameof(ConditionIndicatorColor));
                    OnPropertyChanged(nameof(HasConditionStatus));
                }
            }
        }

        /// <summary>조건 인디케이터 표시 여부 (InputCheck/OutputAction 노드이고 상태가 설정된 경우)</summary>
        public bool HasConditionStatus =>
            _conditionStatus.HasValue &&
            (NodeType == SequenceNodeType.InputCheck || NodeType == SequenceNodeType.OutputAction);

        /// <summary>조건 인디케이터 색상</summary>
        public string ConditionIndicatorColor => _conditionStatus switch
        {
            true => "#4CAF50",
            false => "#F44336",
            _ => "#666666"
        };

        /// <summary>원본 직렬화 설정</summary>
        public SequenceNodeConfig Config { get; set; }

        public SequenceNodeItem(SequenceNodeConfig config)
        {
            Id = config.Id;
            Config = config;
            _nodeType = config.NodeType;
            _name = config.Name;
            _x = config.X;
            _y = config.Y;
        }

        /// <summary>노드 타입별 배경색</summary>
        public string NodeColor => NodeType switch
        {
            SequenceNodeType.Start => "#4CAF50",
            SequenceNodeType.End => "#F44336",
            SequenceNodeType.InputCheck => "#2196F3",
            SequenceNodeType.OutputAction => "#FF9800",
            SequenceNodeType.Inspection => "#9C27B0",
            SequenceNodeType.Branch => "#FFEB3B",
            SequenceNodeType.Delay => "#9E9E9E",
            SequenceNodeType.Repeat => "#00BCD4",
            _ => "#757575"
        };

        /// <summary>노드 타입별 아이콘 (SVG Path Data)</summary>
        public string NodeIcon => NodeType switch
        {
            SequenceNodeType.Start =>
                "M8,5 L8,15 L16,10 Z",
            SequenceNodeType.End =>
                "M6,6 L14,6 L14,14 L6,14 Z",
            SequenceNodeType.InputCheck =>
                "M3,10 L10,3 L17,10 L10,17 Z",
            SequenceNodeType.OutputAction =>
                "M5,4 L15,4 L15,16 L5,16 Z M8,8 L12,8 M8,11 L12,11",
            SequenceNodeType.Inspection =>
                "M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 M10,7 L10,11 L13,13",
            SequenceNodeType.Branch =>
                "M10,3 L17,10 L10,17 L3,10 Z",
            SequenceNodeType.Delay =>
                "M10,3 A7,7 0 1 0 10,17 A7,7 0 1 0 10,3 M10,6 L10,10 L13,10",
            SequenceNodeType.Repeat =>
                "M14,4 A6,6 0 1 1 7,6 M14,4 L14,8 M14,4 L18,4",
            _ => ""
        };

        /// <summary>변경사항을 Config에 동기화</summary>
        public void SyncToConfig()
        {
            Config.Name = Name;
            Config.X = X;
            Config.Y = Y;
            Config.NodeType = NodeType;
        }
    }
}
