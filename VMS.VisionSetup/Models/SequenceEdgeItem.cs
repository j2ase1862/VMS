using CommunityToolkit.Mvvm.ComponentModel;
using VMS.PLC.Models.Sequence;

namespace VMS.VisionSetup.Models
{
    /// <summary>
    /// 시퀀스 연결선 캔버스 표시용 Observable 래퍼 (ToolConnection 패턴 동일)
    /// </summary>
    public class SequenceEdgeItem : ObservableObject
    {
        public string Id { get; }

        private SequenceNodeItem? _sourceNode;
        public SequenceNodeItem? SourceNode
        {
            get => _sourceNode;
            set => SetProperty(ref _sourceNode, value);
        }

        private SequenceNodeItem? _targetNode;
        public SequenceNodeItem? TargetNode
        {
            get => _targetNode;
            set => SetProperty(ref _targetNode, value);
        }

        private string? _label;
        public string? Label
        {
            get => _label;
            set
            {
                if (SetProperty(ref _label, value))
                    OnPropertyChanged(nameof(LineColor));
            }
        }

        /// <summary>연결선 색상 (라벨 기반)</summary>
        public string LineColor => Label switch
        {
            "True" => "#4CAF50",
            "False" => "#F44336",
            _ => "#AAAAAA"
        };

        public SequenceEdgeItem(SequenceEdgeConfig config, SequenceNodeItem? source, SequenceNodeItem? target)
        {
            Id = config.Id;
            _sourceNode = source;
            _targetNode = target;
            _label = config.Label;
        }

        public SequenceEdgeItem(string id, SequenceNodeItem source, SequenceNodeItem target, string? label)
        {
            Id = id;
            _sourceNode = source;
            _targetNode = target;
            _label = label;
        }
    }
}
