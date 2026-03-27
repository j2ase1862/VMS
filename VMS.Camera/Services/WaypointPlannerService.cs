using System.Collections.ObjectModel;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// 웨이포인트 기반 스캔 플래너
    /// 3D 공간에서 촬영 위치를 계획하고, 로봇을 이동시켜 순차 촬영 실행
    /// </summary>
    public class WaypointPlannerService : ObservableObject
    {
        #region Properties

        /// <summary>웨이포인트 목록</summary>
        public ObservableCollection<ScanWaypoint> Waypoints { get; } = new();

        /// <summary>대상 오브젝트 중심점 (mm)</summary>
        private Vector3 _objectCenter;
        public Vector3 ObjectCenter
        {
            get => _objectCenter;
            set => SetProperty(ref _objectCenter, value);
        }

        /// <summary>카메라-대상 거리 (mm)</summary>
        private float _cameraDistance = 500f;
        public float CameraDistance
        {
            get => _cameraDistance;
            set => SetProperty(ref _cameraDistance, MathF.Max(50f, value));
        }

        /// <summary>현재 실행 중인 웨이포인트 인덱스 (-1 = 미실행)</summary>
        private int _currentWaypointIndex = -1;
        public int CurrentWaypointIndex
        {
            get => _currentWaypointIndex;
            private set => SetProperty(ref _currentWaypointIndex, value);
        }

        /// <summary>스캔 실행 중 여부</summary>
        private bool _isScanning;
        public bool IsScanning
        {
            get => _isScanning;
            private set => SetProperty(ref _isScanning, value);
        }

        /// <summary>상태 메시지</summary>
        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            private set => SetProperty(ref _statusMessage, value);
        }

        #endregion

        #region Waypoint Generation

        /// <summary>
        /// 패턴 기반 웨이포인트 자동 생성
        /// </summary>
        /// <param name="pattern">배치 패턴</param>
        /// <param name="count">웨이포인트 수</param>
        /// <param name="center">대상 중심점</param>
        /// <param name="distance">카메라-대상 거리 (mm)</param>
        /// <param name="elevationAngle">수평면 대비 카메라 고도각 (도, Ring용)</param>
        public void GenerateWaypoints(WaypointPattern pattern, int count, Vector3 center, float distance, float elevationAngle = 30f)
        {
            Waypoints.Clear();
            ObjectCenter = center;
            CameraDistance = distance;

            switch (pattern)
            {
                case WaypointPattern.Ring:
                    GenerateRing(count, center, distance, elevationAngle);
                    break;
                case WaypointPattern.TopPlusRing:
                    GenerateTopPlusRing(count, center, distance, elevationAngle);
                    break;
                case WaypointPattern.Hemisphere:
                    GenerateHemisphere(count, center, distance);
                    break;
            }

            StatusMessage = $"{Waypoints.Count}개 웨이포인트 생성됨 ({pattern})";
        }

        /// <summary>수평 링: 대상 주위 등간격 N개</summary>
        private void GenerateRing(int count, Vector3 center, float distance, float elevationDeg)
        {
            float elevRad = elevationDeg * MathF.PI / 180f;
            float horizontalDist = distance * MathF.Cos(elevRad);
            float verticalOffset = distance * MathF.Sin(elevRad);

            for (int i = 0; i < count; i++)
            {
                float angle = 2f * MathF.PI * i / count;

                var pos = new Vector3(
                    center.X + horizontalDist * MathF.Cos(angle),
                    center.Y + horizontalDist * MathF.Sin(angle),
                    center.Z + verticalOffset
                );

                Waypoints.Add(new ScanWaypoint
                {
                    Index = i,
                    Name = $"Ring-{i + 1} ({angle * 180f / MathF.PI:F0}°)",
                    CameraPosition = pos,
                    LookAtTarget = center
                });
            }
        }

        /// <summary>상면 1개 + 수평 링</summary>
        private void GenerateTopPlusRing(int count, Vector3 center, float distance, float elevationDeg)
        {
            // 상면
            Waypoints.Add(new ScanWaypoint
            {
                Index = 0,
                Name = "Top",
                CameraPosition = new Vector3(center.X, center.Y, center.Z + distance),
                LookAtTarget = center
            });

            // 링 (count - 1개)
            int ringCount = Math.Max(1, count - 1);
            float elevRad = elevationDeg * MathF.PI / 180f;
            float horizontalDist = distance * MathF.Cos(elevRad);
            float verticalOffset = distance * MathF.Sin(elevRad);

            for (int i = 0; i < ringCount; i++)
            {
                float angle = 2f * MathF.PI * i / ringCount;

                Waypoints.Add(new ScanWaypoint
                {
                    Index = i + 1,
                    Name = $"Side-{i + 1} ({angle * 180f / MathF.PI:F0}°)",
                    CameraPosition = new Vector3(
                        center.X + horizontalDist * MathF.Cos(angle),
                        center.Y + horizontalDist * MathF.Sin(angle),
                        center.Z + verticalOffset),
                    LookAtTarget = center
                });
            }
        }

        /// <summary>반구형: 위경도 그리드</summary>
        private void GenerateHemisphere(int count, Vector3 center, float distance)
        {
            // 피보나치 격자로 반구 균등 분포
            int idx = 0;
            float goldenAngle = MathF.PI * (3f - MathF.Sqrt(5f));

            for (int i = 0; i < count; i++)
            {
                // 반구 상반부만 (z > 0)
                float t = (float)i / (count - 1); // 0 ~ 1
                float phi = MathF.Acos(1f - t); // 0 ~ PI/2 (상반구)
                phi = MathF.Min(phi, MathF.PI / 2f * 0.85f); // 85도까지 (측면 한계)
                float theta = goldenAngle * i;

                var pos = new Vector3(
                    center.X + distance * MathF.Sin(phi) * MathF.Cos(theta),
                    center.Y + distance * MathF.Sin(phi) * MathF.Sin(theta),
                    center.Z + distance * MathF.Cos(phi)
                );

                Waypoints.Add(new ScanWaypoint
                {
                    Index = idx++,
                    Name = $"Hemi-{idx}",
                    CameraPosition = pos,
                    LookAtTarget = center
                });
            }
        }

        /// <summary>수동 웨이포인트 추가</summary>
        public void AddWaypoint(Vector3 position, Vector3 lookAt, string? name = null)
        {
            var wp = new ScanWaypoint
            {
                Index = Waypoints.Count,
                Name = name ?? $"WP-{Waypoints.Count + 1}",
                CameraPosition = position,
                LookAtTarget = lookAt
            };
            Waypoints.Add(wp);
        }

        /// <summary>웨이포인트 제거</summary>
        public void RemoveWaypoint(int index)
        {
            if (index >= 0 && index < Waypoints.Count)
            {
                Waypoints.RemoveAt(index);
                ReindexWaypoints();
            }
        }

        /// <summary>전체 초기화</summary>
        public void ClearWaypoints()
        {
            Waypoints.Clear();
            CurrentWaypointIndex = -1;
            StatusMessage = "웨이포인트 초기화됨";
        }

        private void ReindexWaypoints()
        {
            for (int i = 0; i < Waypoints.Count; i++)
                Waypoints[i].Index = i;
        }

        #endregion

        #region Scan Execution

        /// <summary>
        /// 전체 웨이포인트 순차 스캔 실행
        /// VMS → Robot: 포즈 전송 → Robot 이동 → 촬영
        /// </summary>
        /// <param name="robot">로봇 서비스</param>
        /// <param name="camera">카메라 서비스</param>
        /// <param name="session">멀티뷰 세션 (결과 저장)</param>
        /// <param name="handEyeInverse">핸드-아이 역행렬 (Identity면 직접 포즈)</param>
        /// <param name="convention">로봇 오일러 방식</param>
        /// <param name="moveCommand">로봇 이동 커맨드 형식 (예: "MOVEJ")</param>
        /// <param name="settlingTimeMs">정지 대기 시간</param>
        /// <param name="ct">취소 토큰</param>
        public async Task<bool> ExecuteScanSequenceAsync(
            IRobotService robot,
            ICameraAcquisition camera,
            MultiViewSession session,
            Matrix4x4 handEyeInverse,
            EulerConvention convention,
            string moveCommand = "MOVEJ",
            int settlingTimeMs = 300,
            CancellationToken ct = default)
        {
            if (Waypoints.Count == 0)
            {
                StatusMessage = "웨이포인트 없음";
                return false;
            }

            IsScanning = true;

            try
            {
                for (int i = 0; i < Waypoints.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    var wp = Waypoints[i];
                    CurrentWaypointIndex = i;
                    StatusMessage = $"[{i + 1}/{Waypoints.Count}] {wp.Name} 이동 중...";

                    // 1. 웨이포인트 → 로봇 포즈 변환
                    var targetPose = wp.ToRobotPose(handEyeInverse, convention);

                    // 2. 로봇에 이동 명령 전송
                    string poseStr = $"{targetPose.X:F2},{targetPose.Y:F2},{targetPose.Z:F2}," +
                                     $"{targetPose.Rx:F4},{targetPose.Ry:F4},{targetPose.Rz:F4}";
                    var response = await robot.SendCommandAsync($"{moveCommand} {poseStr}", 30000);

                    if (response == null)
                    {
                        StatusMessage = $"[{i + 1}] {wp.Name} 로봇 이동 명령 실패";
                        continue;
                    }

                    // 3. 정지 대기
                    await robot.WaitForSettlingAsync(settlingTimeMs);
                    StatusMessage = $"[{i + 1}/{Waypoints.Count}] {wp.Name} 촬영 중...";

                    // 4. 현재 실제 포즈 확인 (로봇에서 읽기)
                    var actualPose = await robot.GetCurrentPoseAsync();
                    var poseForScan = actualPose ?? targetPose;

                    // 5. 촬영
                    var result = await camera.AcquireAsync();
                    if (!result.Success || result.PointCloud == null)
                    {
                        StatusMessage = $"[{i + 1}] {wp.Name} 촬영 실패: {result.Message}";
                        continue;
                    }

                    // 6. 세션에 스캔 데이터 추가
                    session.AddScan(new ScanData
                    {
                        Timestamp = DateTime.Now,
                        Pose = poseForScan,
                        PointCloud = result.PointCloud,
                        Image2D = result.Image2D
                    });

                    wp.IsCompleted = true;
                    StatusMessage = $"[{i + 1}/{Waypoints.Count}] {wp.Name} 완료";
                }

                CurrentWaypointIndex = -1;
                StatusMessage = $"스캔 완료: {session.Scans.Count}/{Waypoints.Count}개 성공";
                return true;
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "스캔 취소됨";
                return false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"스캔 오류: {ex.Message}";
                return false;
            }
            finally
            {
                IsScanning = false;
            }
        }

        /// <summary>
        /// 완료 상태 리셋
        /// </summary>
        public void ResetCompletionStatus()
        {
            foreach (var wp in Waypoints)
                wp.IsCompleted = false;
            CurrentWaypointIndex = -1;
        }

        #endregion
    }
}
