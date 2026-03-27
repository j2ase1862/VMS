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

        // MIL 핸들
        private MIL_ID _milApplication = MIL.M_NULL;
        private MIL_ID _milSystem = MIL.M_NULL;
        private MIL_ID _milDigitizer = MIL.M_NULL;

        // 더블 버퍼링: Grab과 처리를 분리
        private MIL_ID _grabBuffer1 = MIL.M_NULL;
        private MIL_ID _grabBuffer2 = MIL.M_NULL;
        private int _currentGrabBuffer;

        private int _imageWidth;
        private int _imageHeight;
        private int _imageBands;
        private int _imagePitchByte;

        public bool IsConnected { get; private set; }
        public int DownsampleStride { get; set; } = 1;

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

                MIL.MappControl(MIL.M_DEFAULT, MIL.M_ERROR, MIL.M_PRINT_DISABLE);

                // 2. MIL System 할당
                var systemType = ResolveSystemType(camera.ConnectionString);
                long boardDev = camera.BoardNumber >= 0 ? MIL.M_DEV0 + camera.BoardNumber : MIL.M_DEFAULT;
                MIL.MsysAlloc(MIL.M_DEFAULT, systemType, boardDev, MIL.M_DEFAULT, ref _milSystem);

                if (_milSystem == MIL.M_NULL)
                {
                    System.Diagnostics.Debug.WriteLine("MIL System 할당 실패 - 프레임 그래버를 확인하세요.");
                    Cleanup();
                    return false;
                }

                // 3. Digitizer 할당
                long digDev = camera.DigitizerNumber >= 0 ? MIL.M_DEV0 + camera.DigitizerNumber : MIL.M_DEFAULT;
                var dcfPath = !string.IsNullOrWhiteSpace(camera.DcfFilePath) ? camera.DcfFilePath : "M_DEFAULT";
                MIL.MdigAlloc(_milSystem, digDev, dcfPath, MIL.M_DEFAULT, ref _milDigitizer);

                if (_milDigitizer == MIL.M_NULL)
                {
                    System.Diagnostics.Debug.WriteLine("MIL Digitizer 할당 실패 - 카메라 연결을 확인하세요.");
                    Cleanup();
                    return false;
                }

                // 4. DCF 기반 이미지 크기 조회 — DCF 설정을 100% 신뢰
                // (인텔리캠에서 정상 동작 확인된 DCF 설정을 그대로 사용)
                _imageWidth = (int)MIL.MdigInquire(_milDigitizer, MIL.M_SIZE_X, MIL.M_NULL);
                _imageHeight = (int)MIL.MdigInquire(_milDigitizer, MIL.M_SIZE_Y, MIL.M_NULL);
                _imageBands = (int)MIL.MdigInquire(_milDigitizer, MIL.M_SIZE_BAND, MIL.M_NULL);

                // Grab Timeout만 설정 (라인스캔은 프레임 완성까지 시간이 걸림)
                MIL.MdigControl(_milDigitizer, MIL.M_GRAB_TIMEOUT, (MIL_INT)MIL.M_INFINITE);

                camera.Width = _imageWidth;
                camera.Height = _imageHeight;

                // 6. 더블 버퍼 할당
                long imageAttributes = MIL.M_IMAGE + MIL.M_GRAB + MIL.M_PROC;
                AllocImageBuffer(imageAttributes, ref _grabBuffer1);
                AllocImageBuffer(imageAttributes, ref _grabBuffer2);

                if (_grabBuffer1 == MIL.M_NULL || _grabBuffer2 == MIL.M_NULL)
                {
                    System.Diagnostics.Debug.WriteLine("MIL 이미지 버퍼 할당 실패");
                    Cleanup();
                    return false;
                }

                // 7. Pitch 조회 및 캐싱
                long pitch = 0;
                MIL.MbufInquire(_grabBuffer1, MIL.M_PITCH_BYTE, ref pitch);
                _imagePitchByte = (int)pitch;

                IsConnected = true;
                _currentGrabBuffer = 0;
                System.Diagnostics.Debug.WriteLine(
                    $"Matrox MIL 연결 성공: {_imageWidth}x{_imageHeight}, {_imageBands}ch, pitch={_imagePitchByte}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Matrox MIL 카메라 연결 오류: {ex.Message}");
                Cleanup();
                return false;
            }
        }

        private void AllocImageBuffer(long attributes, ref MIL_ID buffer)
        {
            if (_imageBands == 1)
            {
                MIL.MbufAlloc2d(_milSystem, _imageWidth, _imageHeight,
                    8 + MIL.M_UNSIGNED, attributes, ref buffer);
            }
            else
            {
                MIL.MbufAllocColor(_milSystem, _imageBands, _imageWidth, _imageHeight,
                    8 + MIL.M_UNSIGNED, attributes, ref buffer);
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

        public async Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000)
        {
            if (!IsConnected || _milDigitizer == MIL.M_NULL)
            {
                return new AcquisitionResult
                {
                    Success = false,
                    Message = "카메라가 연결되지 않았습니다."
                };
            }

            try
            {
                // 더블 버퍼링: 번갈아가며 Grab
                var grabTarget = _currentGrabBuffer == 0 ? _grabBuffer1 : _grabBuffer2;
                _currentGrabBuffer = 1 - _currentGrabBuffer;

                // Grab 전 버퍼 초기화 (이전 잔상 방지)
                MIL.MbufClear(grabTarget, 0);

                MIL.MdigGrab(_milDigitizer, grabTarget);
                MIL.MdigGrabWait(_milDigitizer, MIL.M_GRAB_END);

                // MbufGet: MIL 내부에서 DMA 동기화 + Pitch 처리 + 캐시 일관성 보장
                // M_HOST_ADDRESS 직접 접근보다 안전하고 정확함
                int dataSize = _imageWidth * _imageHeight * _imageBands;
                byte[] pixelData = new byte[dataSize];

                if (_imageBands == 1)
                {
                    MIL.MbufGet2d(grabTarget, 0, 0, (MIL_INT)_imageWidth, (MIL_INT)_imageHeight, pixelData);
                }
                else
                {
                    MIL.MbufGet(grabTarget, pixelData);
                }

                // byte[] → Mat 변환
                Mat fullMat;
                if (_imageBands == 1)
                {
                    fullMat = Mat.FromPixelData(_imageHeight, _imageWidth, MatType.CV_8UC1, pixelData);
                }
                else
                {
                    // MbufGet은 Band-Interleaved로 반환 (Pitch 없음)
                    fullMat = Mat.FromPixelData(_imageHeight, _imageWidth, MatType.CV_8UC3, pixelData);
                    Cv2.CvtColor(fullMat, fullMat, ColorConversionCodes.RGB2BGR);
                }

                Mat resultMat;
                if (DownsampleStride > 1)
                {
                    // Live 모드: 축소 복사본만 생성
                    int newW = _imageWidth / DownsampleStride;
                    int newH = _imageHeight / DownsampleStride;
                    resultMat = new Mat();
                    Cv2.Resize(fullMat, resultMat, new Size(newW, newH), 0, 0, InterpolationFlags.Nearest);
                    fullMat.Dispose();
                }
                else
                {
                    resultMat = fullMat;
                }

                return new AcquisitionResult
                {
                    Success = true,
                    Image2D = resultMat,
                    Message = $"Matrox 획득 완료 ({resultMat.Width}x{resultMat.Height}, {_imageBands}ch)"
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

        private static string ResolveSystemType(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString))
                return MIL.M_SYSTEM_DEFAULT;

            var conn = connectionString.Trim().ToUpperInvariant();

            if (conn.StartsWith("M_SYSTEM_"))
                return connectionString.Trim();

            return conn switch
            {
                "SOLIOS" => MIL.M_SYSTEM_SOLIOS,
                "RAPIXO" => MIL.M_SYSTEM_RAPIXOCXP,
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

        private void Cleanup()
        {
            try
            {
                if (_grabBuffer1 != MIL.M_NULL)
                {
                    MIL.MbufFree(_grabBuffer1);
                    _grabBuffer1 = MIL.M_NULL;
                }

                if (_grabBuffer2 != MIL.M_NULL)
                {
                    MIL.MbufFree(_grabBuffer2);
                    _grabBuffer2 = MIL.M_NULL;
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
