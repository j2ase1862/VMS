#if MIL_AVAILABLE
using Matrox.MatroxImagingLibrary;
#endif
using OpenCvSharp;
using System.Runtime.InteropServices;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// Matrox MIL (Matrox Imaging Library) 프레임 그래버 연동 구현
    /// MIL_AVAILABLE 심볼이 없으면 컴파일에서 제외됨
    /// </summary>
#if MIL_AVAILABLE
    public class MatroxCameraAcquisition : ICameraAcquisition
    {
        private Models.CameraInfo? _camera;
        private bool _disposed;

        // MIL 핸들 (MIL_ID = IntPtr 또는 long)
        private MIL_ID _milApplication = MIL.M_NULL;
        private MIL_ID _milSystem = MIL.M_NULL;
        private MIL_ID _milDigitizer = MIL.M_NULL;
        private MIL_ID _milImage = MIL.M_NULL;

        private int _imageWidth;
        private int _imageHeight;
        private int _imageBands;

        public bool IsConnected { get; private set; }

        public async Task<bool> ConnectAsync(Models.CameraInfo camera)
        {
            _camera = camera;

            try
            {
                // 1. MIL Application 할당
                MIL.MappAlloc(MIL.M_NULL, MIL.M_DEFAULT, ref _milApplication);

                if (_milApplication == MIL.M_NULL)
                {
                    System.Diagnostics.Debug.WriteLine("MIL Application 할당 실패");
                    return false;
                }

                // 에러 발생 시 예외 대신 로그만 출력하도록 설정
                MIL.MappControl(MIL.M_DEFAULT, MIL.M_ERROR, MIL.M_PRINT_DISABLE);

                // 2. MIL System 할당 (프레임 그래버 보드)
                // ConnectionString으로 시스템 타입 지정 가능 (예: "M_SYSTEM_SOLIOS", "M_SYSTEM_RAPIXO")
                // 비어있으면 M_SYSTEM_DEFAULT (자동 탐지)
                var systemType = ResolveSystemType(camera.ConnectionString);
                MIL.MsysAlloc(MIL.M_DEFAULT, systemType, MIL.M_DEFAULT, MIL.M_DEFAULT, ref _milSystem);

                if (_milSystem == MIL.M_NULL)
                {
                    System.Diagnostics.Debug.WriteLine("MIL System 할당 실패 - 프레임 그래버를 확인하세요.");
                    Cleanup();
                    return false;
                }

                // 3. Digitizer 할당 (카메라 인터페이스)
                MIL.MdigAlloc(_milSystem, MIL.M_DEFAULT, "M_DEFAULT", MIL.M_DEFAULT, ref _milDigitizer);

                if (_milDigitizer == MIL.M_NULL)
                {
                    System.Diagnostics.Debug.WriteLine("MIL Digitizer 할당 실패 - 카메라 연결을 확인하세요.");
                    Cleanup();
                    return false;
                }

                // 4. Digitizer에서 이미지 크기 조회
                _imageWidth = (int)MIL.MdigInquire(_milDigitizer, MIL.M_SIZE_X, MIL.M_NULL);
                _imageHeight = (int)MIL.MdigInquire(_milDigitizer, MIL.M_SIZE_Y, MIL.M_NULL);
                _imageBands = (int)MIL.MdigInquire(_milDigitizer, MIL.M_SIZE_BAND, MIL.M_NULL);

                // CameraInfo에 해상도 반영
                camera.Width = _imageWidth;
                camera.Height = _imageHeight;

                // 5. 이미지 버퍼 할당
                long imageAttributes = MIL.M_IMAGE + MIL.M_GRAB + MIL.M_PROC;
                if (_imageBands == 1)
                {
                    MIL.MbufAlloc2d(_milSystem, _imageWidth, _imageHeight,
                        8 + MIL.M_UNSIGNED, imageAttributes, ref _milImage);
                }
                else
                {
                    MIL.MbufAllocColor(_milSystem, _imageBands, _imageWidth, _imageHeight,
                        8 + MIL.M_UNSIGNED, imageAttributes, ref _milImage);
                }

                if (_milImage == MIL.M_NULL)
                {
                    System.Diagnostics.Debug.WriteLine("MIL 이미지 버퍼 할당 실패");
                    Cleanup();
                    return false;
                }

                IsConnected = true;
                System.Diagnostics.Debug.WriteLine(
                    $"Matrox MIL 카메라 연결 성공: {_imageWidth}x{_imageHeight}, {_imageBands}ch");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Matrox MIL 카메라 연결 오류: {ex.Message}");
                Cleanup();
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                Cleanup();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Matrox MIL 카메라 연결 해제 오류: {ex.Message}");
            }
            finally
            {
                IsConnected = false;
            }
        }

        public async Task<AcquisitionResult> AcquireAsync()
        {
            if (!IsConnected || _milDigitizer == MIL.M_NULL || _milImage == MIL.M_NULL)
            {
                return new AcquisitionResult
                {
                    Success = false,
                    Message = "카메라가 연결되지 않았습니다."
                };
            }

            try
            {
                // 영상 취득 (동기 Grab)
                MIL.MdigGrab(_milDigitizer, _milImage);
                MIL.MdigGrabWait(_milDigitizer, MIL.M_GRAB_END);

                // 호스트 메모리 주소 획득
                IntPtr pData = IntPtr.Zero;
                MIL.MbufInquire(_milImage, MIL.M_HOST_ADDRESS, ref pData);

                if (pData == IntPtr.Zero)
                {
                    return new AcquisitionResult
                    {
                        Success = false,
                        Message = "MIL 이미지 버퍼 주소를 가져올 수 없습니다."
                    };
                }

                Mat mat;

                if (_imageBands == 1)
                {
                    // 그레이스케일: MIL 버퍼에서 OpenCV Mat으로 복사
                    mat = new Mat(_imageHeight, _imageWidth, MatType.CV_8UC1);
                    int dataSize = _imageWidth * _imageHeight;
                    unsafe
                    {
                        Buffer.MemoryCopy(pData.ToPointer(), mat.Data.ToPointer(), dataSize, dataSize);
                    }
                }
                else
                {
                    // 컬러: MIL은 Band-Planar (RR..GG..BB..) 형식이므로 변환 필요
                    mat = ConvertPlanarToBgr(pData, _imageWidth, _imageHeight, _imageBands);
                }

                return new AcquisitionResult
                {
                    Success = true,
                    Image2D = mat,
                    Message = $"Matrox 획득 완료 ({_imageWidth}x{_imageHeight}, {_imageBands}ch)"
                };
            }
            catch (Exception ex)
            {
                return new AcquisitionResult
                {
                    Success = false,
                    Message = $"획득 오류: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// MIL Band-Planar(RR..GG..BB..) → OpenCV BGR Interleaved 변환
        /// </summary>
        private static Mat ConvertPlanarToBgr(IntPtr pData, int width, int height, int bands)
        {
            int planeSize = width * height;
            var mat = new Mat(height, width, MatType.CV_8UC3);

            unsafe
            {
                byte* src = (byte*)pData.ToPointer();
                byte* dst = (byte*)mat.Data.ToPointer();

                byte* planeR = src;                    // Band 0: Red
                byte* planeG = src + planeSize;        // Band 1: Green
                byte* planeB = src + planeSize * 2;    // Band 2: Blue

                for (int i = 0; i < planeSize; i++)
                {
                    // OpenCV BGR 순서
                    dst[i * 3 + 0] = planeB[i];
                    dst[i * 3 + 1] = planeG[i];
                    dst[i * 3 + 2] = planeR[i];
                }
            }

            return mat;
        }

        /// <summary>
        /// ConnectionString에서 MIL 시스템 타입 결정
        /// </summary>
        private static string ResolveSystemType(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return MIL.M_SYSTEM_DEFAULT;

            var conn = connectionString.Trim().ToUpperInvariant();

            // 직접 MIL 시스템 상수명을 입력한 경우
            if (conn.StartsWith("M_SYSTEM_"))
                return connectionString.Trim();

            // 보드 이름으로 매핑
            return conn switch
            {
                "SOLIOS" => MIL.M_SYSTEM_SOLIOS,
                "RAPIXO" => MIL.M_SYSTEM_RAPIXO,
                "RADIENT" => MIL.M_SYSTEM_RADIENT,
                "ORION" => MIL.M_SYSTEM_ORION_HD,
                "MORPHIS" => MIL.M_SYSTEM_MORPHIS,
                "IRIS" => MIL.M_SYSTEM_IRIS_GT,
                "HOST" => MIL.M_SYSTEM_HOST,
                "GIGE" => MIL.M_SYSTEM_GIGE_VISION,
                "USB3" => MIL.M_SYSTEM_USB3_VISION,
                "GENTL" => MIL.M_SYSTEM_GENTL,
                _ => MIL.M_SYSTEM_DEFAULT
            };
        }

        /// <summary>
        /// MIL 리소스 정리
        /// </summary>
        private void Cleanup()
        {
            try
            {
                if (_milImage != MIL.M_NULL)
                {
                    MIL.MbufFree(_milImage);
                    _milImage = MIL.M_NULL;
                }

                if (_milDigitizer != MIL.M_NULL)
                {
                    MIL.MdigFree(_milDigitizer);
                    _milDigitizer = MIL.M_NULL;
                }

                if (_milSystem != MIL.M_NULL)
                {
                    MIL.MsysFree(_milSystem);
                    _milSystem = MIL.M_NULL;
                }

                if (_milApplication != MIL.M_NULL)
                {
                    MIL.MappFree(_milApplication);
                    _milApplication = MIL.M_NULL;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MIL 리소스 정리 오류: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Cleanup();
                IsConnected = false;
            }
        }
    }
#endif
}
