using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;
using VMS.VisionSetup.VisionTools.Measurement;
using VMS.VisionSetup.VisionTools.PointCloud;

namespace VMS.VisionSetup.ViewModels.ToolSettings
{
    /// <summary>클러스터 콤보 항목 — 번호 + 요약 라벨.</summary>
    public class ClusterOption
    {
        public int Index { get; init; }
        public string Label { get; init; } = string.Empty;
    }

    public class Geometry3DToolSettingsViewModel : ToolSettingsViewModelBase
    {
        private Geometry3DTool TypedTool => (Geometry3DTool)Tool;

        public Geometry3DToolSettingsViewModel(Geometry3DTool tool) : base(tool)
        {
            RefreshClusterOptions();
        }

        public override bool HasCustomROISection => true;

        public Geometry3DOperation Operation
        {
            get => TypedTool.Operation;
            set
            {
                TypedTool.Operation = value;
                OnPropertyChanged();
                NotifyVisibilityChanged();
            }
        }

        public Array Operations => Enum.GetValues(typeof(Geometry3DOperation));

        // ── 포인트 소스 모드 (라디오 상호 배타) ──

        public bool UseManualPoints
        {
            get => TypedTool.UseManualPoints;
            set
            {
                TypedTool.UseManualPoints = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(UseConnectedSource));
                NotifyVisibilityChanged();
            }
        }

        /// <summary>라디오 B(연결 소스) 바인딩용 — UseManualPoints의 반대.</summary>
        public bool UseConnectedSource
        {
            get => !UseManualPoints;
            set => UseManualPoints = !value;
        }

        // ── Operation별 표시 제어 ──
        // Source Index는 점을 쓰는 연산에만 의미가 있고(B는 PointToPoint 전용),
        // 평면 연산(PlaneToPlane*)에서는 포인트 소스 선택 자체를 숨긴다.

        private bool IsPointOperation =>
            Operation == Geometry3DOperation.PointToPointDistance ||
            Operation == Geometry3DOperation.PointToPlaneDistance ||
            Operation == Geometry3DOperation.PointToLineDistance3D;

        public bool ShowPointSourceSelector => IsPointOperation;
        public bool ShowManualPoints => UseManualPoints && IsPointOperation;
        public bool ShowPointB => ShowManualPoints && Operation == Geometry3DOperation.PointToPointDistance;
        public bool ShowSourceSection => !UseManualPoints && IsPointOperation;
        public bool ShowSourceB => ShowSourceSection && Operation == Geometry3DOperation.PointToPointDistance;

        private void NotifyVisibilityChanged()
        {
            OnPropertyChanged(nameof(ShowPointSourceSelector));
            OnPropertyChanged(nameof(ShowManualPoints));
            OnPropertyChanged(nameof(ShowPointB));
            OnPropertyChanged(nameof(ShowSourceSection));
            OnPropertyChanged(nameof(ShowSourceB));
        }

        // ── 수동 포인트 ──

        public double PointAX
        {
            get => TypedTool.PointA.X;
            set { TypedTool.PointA = new OpenCvSharp.Point2d(value, TypedTool.PointA.Y); OnPropertyChanged(); }
        }

        public double PointAY
        {
            get => TypedTool.PointA.Y;
            set { TypedTool.PointA = new OpenCvSharp.Point2d(TypedTool.PointA.X, value); OnPropertyChanged(); }
        }

        public double PointBX
        {
            get => TypedTool.PointB.X;
            set { TypedTool.PointB = new OpenCvSharp.Point2d(value, TypedTool.PointB.Y); OnPropertyChanged(); }
        }

        public double PointBY
        {
            get => TypedTool.PointB.Y;
            set { TypedTool.PointB = new OpenCvSharp.Point2d(TypedTool.PointB.X, value); OnPropertyChanged(); }
        }

        // ── 클러스터 소스 선택 (콤보) ──
        // nullable: 목록 갱신 순간 ComboBox가 SelectedValue=null을 밀어넣는데,
        // int 속성이면 변환 실패로 검증 오류(빨간 테두리)가 뜬다 — null은 무시.

        public int? SelectedClusterA
        {
            get => TypedTool.SourceAClusterIndex;
            set
            {
                if (value is not int v) return;
                TypedTool.SourceAClusterIndex = v;
                OnPropertyChanged();
                SendHighlight(v);
            }
        }

        public int? SelectedClusterB
        {
            get => TypedTool.SourceBClusterIndex;
            set
            {
                if (value is not int v) return;
                TypedTool.SourceBClusterIndex = v;
                OnPropertyChanged();
                SendHighlight(v);
            }
        }

        /// <summary>같은 항목 재선택(값 불변 → setter 미호출)에도 강조 표시 — 드롭다운 닫힘에서 호출.</summary>
        public void HighlightClusterA() => SendHighlight(TypedTool.SourceAClusterIndex);
        public void HighlightClusterB() => SendHighlight(TypedTool.SourceBClusterIndex);

        /// <summary>
        /// Result로 연결된 첫 번째 PointCloudClusterTool (없으면 null).
        /// </summary>
        private PointCloudClusterTool? ConnectedClusterTool =>
            VisionService.Instance.GetConnectedSources(Tool, ConnectionType.Result)
                .OfType<PointCloudClusterTool>()
                .FirstOrDefault();

        /// <summary>
        /// 클러스터 콤보 항목 — 인스턴스를 유지하고 내용만 제자리 갱신
        /// (ItemsSource 통째 교체는 선택 해제 → null 푸시를 유발).
        /// </summary>
        public ObservableCollection<ClusterOption> ClusterOptions { get; } = new();

        public bool HasClusterOptions => ClusterOptions.Count > 0;

        /// <summary>콤보 드롭다운 열기 등에서 목록 최신화 (Run 후 결과 반영) + 선택 복원.</summary>
        public void RefreshClusterOptions()
        {
            ClusterOptions.Clear();
            foreach (var option in BuildClusterOptions())
                ClusterOptions.Add(option);

            OnPropertyChanged(nameof(HasClusterOptions));
            // Clear()로 풀린 콤보 선택을 도구의 현재 인덱스로 복원
            OnPropertyChanged(nameof(SelectedClusterA));
            OnPropertyChanged(nameof(SelectedClusterB));
        }

        /// <summary>연결된 Cluster 툴의 직전 실행 결과에서 콤보 항목 생성 (Run 전이면 빈 목록).</summary>
        private List<ClusterOption> BuildClusterOptions()
        {
            var cluster = ConnectedClusterTool;
            var data = cluster?.LastResult?.Success == true ? cluster.LastResult.Data : null;
            if (data == null || !data.TryGetValue("ClusterCount", out var cc))
                return new List<ClusterOption>();

            int count = Convert.ToInt32(cc);
            var options = new List<ClusterOption>(count);
            for (int i = 0; i < count; i++)
            {
                if (!data.TryGetValue($"Cluster{i}_Points", out var pts))
                    break; // MaxReportedClusters 초과분은 Data에 없음

                string label = $"#{i} — {Convert.ToInt64(pts):N0}pt";
                if (data.TryGetValue($"Cluster{i}_Length", out var len) &&
                    data.TryGetValue($"Cluster{i}_Width", out var wid))
                    label += $", {Convert.ToDouble(len):F1}x{Convert.ToDouble(wid):F1}";
                options.Add(new ClusterOption { Index = i, Label = label });
            }
            return options;
        }

        private void SendHighlight(int clusterIndex)
        {
            var cluster = ConnectedClusterTool;
            if (cluster != null)
                WeakReferenceMessenger.Default.Send(
                    new RequestHighlightClusterMessage(cluster.Id, clusterIndex));
        }

        public ObservableCollection<SourceGeometry3D> SourceGeometries => new(TypedTool.SourceGeometries);

        public void RefreshSourceGeometries()
        {
            OnPropertyChanged(nameof(SourceGeometries));
        }
    }
}
