using System.Diagnostics;
using System.IO;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;
using VMS.Camera.Utils;

namespace VMS.Camera.Services
{
    /// <summary>
    /// 멀티뷰 포인트 클라우드 정합 세션
    /// 로봇 이동 → 다중 촬영 → 좌표 변환 → 병합 → (선택적 ICP) → 최종 결과 생성
    /// </summary>
    public class MultiViewSession : ObservableObject, IDisposable
    {
        private bool _disposed;

        #region Properties

        /// <summary>수집된 스캔 데이터 목록</summary>
        private readonly List<ScanData> _scans = new();
        public IReadOnlyList<ScanData> Scans => _scans;

        /// <summary>핸드-아이 캘리브레이션 행렬 (T_tcp_cam). 사전에 구해서 설정.</summary>
        private Matrix4x4 _handEyeMatrix = Matrix4x4.Identity;
        public Matrix4x4 HandEyeMatrix
        {
            get => _handEyeMatrix;
            set => SetProperty(ref _handEyeMatrix, value);
        }

        /// <summary>정합 전략</summary>
        private RegistrationStrategy _strategy = RegistrationStrategy.PoseOnly;
        public RegistrationStrategy Strategy
        {
            get => _strategy;
            set => SetProperty(ref _strategy, value);
        }

        /// <summary>ICP 최대 반복 횟수</summary>
        public int IcpMaxIterations { get; set; } = 50;

        /// <summary>ICP 수렴 임계값 (mm)</summary>
        public float IcpTolerance { get; set; } = 0.01f;

        /// <summary>Voxel Grid 크기 (mm, 0이면 필터 미적용)</summary>
        public float VoxelSize { get; set; } = 1.0f;

        /// <summary>SOR 활성화 여부</summary>
        public bool EnableOutlierRemoval { get; set; } = true;

        /// <summary>SOR k-최근접 이웃 수</summary>
        public int SorK { get; set; } = 20;

        /// <summary>SOR 표준편차 배수</summary>
        public double SorStddevMultiplier { get; set; } = 2.0;

        /// <summary>현재 상태 메시지</summary>
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        /// <summary>진행률 (0.0 ~ 1.0)</summary>
        private double _progress;
        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        /// <summary>최종 통합 포인트 클라우드</summary>
        private PointCloudData? _mergedPointCloud;
        public PointCloudData? MergedPointCloud
        {
            get => _mergedPointCloud;
            private set => SetProperty(ref _mergedPointCloud, value);
        }

        /// <summary>기준 스캔 인덱스 (ICP 정합 시 Reference)</summary>
        public int ReferenceIndex { get; set; } = 0;

        #endregion

        #region Scan Collection

        /// <summary>
        /// 스캔 데이터 추가 (카메라 촬영 + 로봇 포즈 1쌍)
        /// </summary>
        public void AddScan(ScanData scan)
        {
            scan.Index = _scans.Count;
            _scans.Add(scan);
            StatusMessage = $"스캔 {_scans.Count}개 수집됨";
        }

        /// <summary>
        /// 로봇 서비스 + 카메라로 자동 스캔 수행
        /// 로봇이 이미 해당 위치로 이동 완료된 상태에서 호출
        /// </summary>
        public async Task<ScanData?> CaptureAtCurrentPoseAsync(
            IRobotService robot, ICameraAcquisition camera, int settlingTimeMs = 200)
        {
            // 1. 로봇 정지 대기 (구조광 카메라 진동 방지)
            await robot.WaitForSettlingAsync(settlingTimeMs);

            // 2. 로봇 포즈 수집
            var pose = await robot.GetCurrentPoseAsync();
            if (pose == null)
            {
                StatusMessage = "로봇 포즈 수집 실패";
                return null;
            }

            // 3. 카메라 촬영
            var result = await camera.AcquireAsync();
            if (!result.Success || result.PointCloud == null)
            {
                StatusMessage = $"카메라 촬영 실패: {result.Message}";
                return null;
            }

            // 4. 데이터 페어링
            var scan = new ScanData
            {
                Timestamp = DateTime.Now,
                Pose = pose,
                PointCloud = result.PointCloud,
                Image2D = result.Image2D
            };

            AddScan(scan);
            return scan;
        }

        /// <summary>스캔 데이터 초기화</summary>
        public void ClearScans()
        {
            foreach (var scan in _scans)
                scan.Dispose();
            _scans.Clear();
            MergedPointCloud?.Dispose();
            MergedPointCloud = null;
            Progress = 0;
            StatusMessage = "세션 초기화됨";
        }

        #endregion

        #region Processing Pipeline

        /// <summary>
        /// 전체 처리 파이프라인 실행:
        /// 1. 좌표 변환 (Phase 3)
        /// 2. 노이즈 제거 (Phase 4 전처리)
        /// 3. Voxel Grid 다운샘플링
        /// 4. (선택적) ICP 정합 (Phase 4)
        /// 5. 최종 병합
        /// </summary>
        public async Task<PointCloudData?> ProcessAsync(CancellationToken cancellationToken = default)
        {
            if (_scans.Count == 0)
            {
                StatusMessage = "스캔 데이터 없음";
                return null;
            }

            return await Task.Run(() => ProcessInternal(cancellationToken), cancellationToken);
        }

        private PointCloudData? ProcessInternal(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            int totalSteps = _scans.Count * 3 + 2; // 변환 + 필터 + ICP + 병합 + 최종필터
            int currentStep = 0;

            // === Phase 3: 좌표 변환 ===
            StatusMessage = "좌표 변환 중...";
            var transformedClouds = new List<PointCloudData>();

            for (int i = 0; i < _scans.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var scan = _scans[i];
                if (scan.PointCloud == null) continue;

                // M_i = T_base_tcp_i × T_tcp_cam (핸드-아이)
                var finalTransform = TransformUtils.ComposeFinalTransform(scan.Pose, HandEyeMatrix);
                var transformed = TransformUtils.TransformPointCloud(scan.PointCloud, finalTransform);
                scan.TransformedPointCloud = transformed;
                transformedClouds.Add(transformed);

                currentStep++;
                Progress = (double)currentStep / totalSteps;
                StatusMessage = $"좌표 변환: {i + 1}/{_scans.Count}";
            }

            if (transformedClouds.Count == 0)
            {
                StatusMessage = "유효한 포인트 클라우드 없음";
                return null;
            }

            // === Phase 4 전처리: 노이즈 제거 + 다운샘플링 ===
            var processedClouds = new List<PointCloudData>();

            for (int i = 0; i < transformedClouds.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var cloud = transformedClouds[i];

                // SOR 노이즈 제거
                if (EnableOutlierRemoval)
                {
                    cloud = TransformUtils.StatisticalOutlierRemoval(cloud, SorK, SorStddevMultiplier);
                }

                // Voxel Grid 다운샘플링
                if (VoxelSize > 0)
                {
                    cloud = TransformUtils.VoxelGridFilter(cloud, VoxelSize);
                }

                processedClouds.Add(cloud);

                currentStep++;
                Progress = (double)currentStep / totalSteps;
                StatusMessage = $"전처리: {i + 1}/{transformedClouds.Count}";
            }

            // === Phase 4: ICP 정합 (선택적) ===
            if (Strategy == RegistrationStrategy.PoseWithICP && processedClouds.Count >= 2)
            {
                StatusMessage = "ICP 정합 중...";
                int refIdx = Math.Clamp(ReferenceIndex, 0, processedClouds.Count - 1);
                var reference = processedClouds[refIdx];

                for (int i = 0; i < processedClouds.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    if (i == refIdx) { currentStep++; continue; }

                    var icpTransform = TransformUtils.ICP(reference, processedClouds[i],
                        IcpMaxIterations, IcpTolerance);
                    processedClouds[i] = TransformUtils.TransformPointCloud(processedClouds[i], icpTransform);

                    currentStep++;
                    Progress = (double)currentStep / totalSteps;
                    StatusMessage = $"ICP 정합: {i + 1}/{processedClouds.Count}";
                }
            }
            else
            {
                currentStep += processedClouds.Count;
            }

            // === 최종 병합 ===
            StatusMessage = "포인트 클라우드 병합 중...";
            var merged = TransformUtils.MergePointClouds(processedClouds, "MultiView_Merged");
            currentStep++;

            // 최종 Voxel 필터 (병합 후 중첩 영역 정리)
            if (VoxelSize > 0)
            {
                merged = TransformUtils.VoxelGridFilter(merged, VoxelSize);
            }
            currentStep++;

            Progress = 1.0;
            sw.Stop();
            StatusMessage = $"완료: {merged.PointCount:N0}pts, {sw.ElapsedMilliseconds}ms";

            MergedPointCloud = merged;
            return merged;
        }

        #endregion

        #region Serialization

        /// <summary>
        /// 핸드-아이 행렬을 파일로 저장 (16 float values)
        /// </summary>
        public void SaveHandEyeMatrix(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Create);
            using var bw = new BinaryWriter(fs);

            bw.Write(HandEyeMatrix.M11); bw.Write(HandEyeMatrix.M12); bw.Write(HandEyeMatrix.M13); bw.Write(HandEyeMatrix.M14);
            bw.Write(HandEyeMatrix.M21); bw.Write(HandEyeMatrix.M22); bw.Write(HandEyeMatrix.M23); bw.Write(HandEyeMatrix.M24);
            bw.Write(HandEyeMatrix.M31); bw.Write(HandEyeMatrix.M32); bw.Write(HandEyeMatrix.M33); bw.Write(HandEyeMatrix.M34);
            bw.Write(HandEyeMatrix.M41); bw.Write(HandEyeMatrix.M42); bw.Write(HandEyeMatrix.M43); bw.Write(HandEyeMatrix.M44);
        }

        /// <summary>
        /// 핸드-아이 행렬을 파일에서 로드
        /// </summary>
        public void LoadHandEyeMatrix(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open);
            using var br = new BinaryReader(fs);

            HandEyeMatrix = new Matrix4x4(
                br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
                br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
                br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle(),
                br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle()
            );
        }

        #endregion

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var scan in _scans)
                scan.Dispose();
            _scans.Clear();

            MergedPointCloud?.Dispose();
        }
    }
}
