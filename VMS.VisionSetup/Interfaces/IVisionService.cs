using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using OpenCvSharp;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Interfaces
{
    public interface IVisionService
    {
        Mat? CurrentImage { get; set; }
        Mat? CurrentDepthMap32F { get; }
        ObservableCollection<VisionToolBase> Tools { get; }
        ObservableCollection<VisionResult> Results { get; }
        double TotalExecutionTime { get; }
        bool IsRunning { get; }
        bool LastRunSuccess { get; }
        Mat? LastCompositeOverlay { get; }

        /// <summary>
        /// 실행이 쓰는 현재 점군 — 점군 도구(Mask Crop · Cluster · Filter · Registration · Deviation)가
        /// 실행 중 바꾼 결과가 여기 남는다. 3D 뷰가 실행 결과를 보일 때 읽는다.
        /// </summary>
        PointCloudData? CurrentPointCloud { get; }

        /// <summary>마지막 실행의 파이프라인 경고 (연결 사이클, 이미지 연결 폴백 등). null이면 경고 없음.</summary>
        string? LastPipelineWarning { get; }

        /// <summary>마지막 ExecuteAll의 도구 ID → 결과 매핑 (topological sort 영향 없이 안전 lookup).</summary>
        Dictionary<string, VisionResult> LastExecutionResultsById { get; }

        /// <summary>
        /// 현재 워크스페이스 스텝의 Resolution (mm/px) — 캘리브레이션이 없을 때 측정 도구
        /// mm 변환 폴백. MainViewModel 이 스텝 로드/실행 시점에 갱신. 0 = 폴백 없음.
        /// </summary>
        double CurrentStepResolutionMmPerPx { get; set; }

        /// <summary>현재 워크스페이스 스텝 Id — StepPoseStore 기록 키 (다중 스텝 얼라인).</summary>
        string? CurrentStepId { get; set; }

        void SetImage(Mat image);
        void AddTool(VisionToolBase tool);
        void RemoveTool(VisionToolBase tool);
        void MoveTool(int fromIndex, int toIndex);
        void ClearTools();
        void AddConnection(VisionToolBase source, VisionToolBase target, ConnectionType type);
        void RemoveConnection(VisionToolBase source, VisionToolBase target, ConnectionType type);
        void ClearConnections();
        VisionResult ExecuteTool(VisionToolBase tool, Mat? inputImage = null);
        Task<List<VisionResult>> ExecuteAllAsync();
        (Mat HeightMap8U, Mat DepthMap32F, HeightMapMetadata Metadata) GenerateHeightMap(
            PointCloudData pointCloud, float zRef, float zMin, float zMax);
    }
}
