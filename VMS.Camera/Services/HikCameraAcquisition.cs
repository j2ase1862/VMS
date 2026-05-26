#if HIK_AVAILABLE
using MvCamCtrl.NET;
#endif
using OpenCvSharp;
using System.Net;
using System.Runtime.InteropServices;
using VMS.Camera.Interfaces;
using VMS.Camera.Models;

namespace VMS.Camera.Services
{
    /// <summary>
    /// Hikrobot MVS SDK 연동 — GigE/USB3 카메라.
    /// HIK_AVAILABLE 심볼이 정의되고 MvCamCtrl.NET.dll 참조가 있을 때만 컴파일됨.
    /// IP 주소(GigE) 또는 시리얼 번호(USB)로 카메라 매칭.
    /// </summary>
#if HIK_AVAILABLE
    public class HikCameraAcquisition : ICameraAcquisition
    {
        private Models.CameraInfo? _camera;
        private MyCamera? _cam;
        private bool _isGrabbing;
        private bool _disposed;

        public bool IsConnected { get; private set; }

        public async Task<bool> ConnectAsync(Models.CameraInfo camera)
        {
            _camera = camera;

            try
            {
                // 1. Enumerate GigE + USB devices
                var devList = new MyCamera.MV_CC_DEVICE_INFO_LIST();
                int ret = MyCamera.MV_CC_EnumDevices_NET(
                    MyCamera.MV_GIGE_DEVICE | MyCamera.MV_USB_DEVICE, ref devList);
                if (ret != MyCamera.MV_OK || devList.nDeviceNum == 0)
                {
                    System.Diagnostics.Debug.WriteLine("Hikrobot 카메라를 찾을 수 없습니다.");
                    return false;
                }

                // 2. Match by IP (ConnectionString) or Serial
                MyCamera.MV_CC_DEVICE_INFO? matched = null;
                for (uint i = 0; i < devList.nDeviceNum; i++)
                {
                    var dev = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(
                        devList.pDeviceInfo[i], typeof(MyCamera.MV_CC_DEVICE_INFO))!;

                    if (TryMatch(dev, camera))
                    {
                        matched = dev;
                        break;
                    }
                }
                // Fallback: first device
                if (matched == null)
                {
                    matched = (MyCamera.MV_CC_DEVICE_INFO)Marshal.PtrToStructure(
                        devList.pDeviceInfo[0], typeof(MyCamera.MV_CC_DEVICE_INFO))!;
                }

                // 3. Create handle + open
                _cam = new MyCamera();
                var matchedDev = matched.Value;
                ret = _cam.MV_CC_CreateDevice_NET(ref matchedDev);
                if (ret != MyCamera.MV_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"Hikrobot CreateDevice 실패: 0x{ret:X}");
                    return false;
                }

                ret = _cam.MV_CC_OpenDevice_NET();
                if (ret != MyCamera.MV_OK)
                {
                    System.Diagnostics.Debug.WriteLine($"Hikrobot OpenDevice 실패: 0x{ret:X}");
                    _cam.MV_CC_DestroyDevice_NET();
                    _cam = null;
                    return false;
                }

                // 4. GigE: packet size 자동 조정 (성능)
                if ((matchedDev.nTLayerType & MyCamera.MV_GIGE_DEVICE) != 0)
                {
                    int optimal = _cam.MV_CC_GetOptimalPacketSize_NET();
                    if (optimal > 0)
                        _cam.MV_CC_SetIntValueEx_NET("GevSCPSPacketSize", optimal);
                }

                // 5. Trigger mode off (소프트웨어로 한 프레임씩 가져옴)
                _cam.MV_CC_SetEnumValue_NET("TriggerMode", 0);

                // 6. Apply camera parameters (exposure / gain)
                ApplyCameraParameters();

                IsConnected = true;
                System.Diagnostics.Debug.WriteLine("Hikrobot 카메라 연결 성공");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hikrobot 연결 오류: {ex.Message}");
                Cleanup();
                return false;
            }
        }

        public Task DisconnectAsync()
        {
            Cleanup();
            return Task.CompletedTask;
        }

        public Task<AcquisitionResult> AcquireAsync(int timeoutMs = 5000)
        {
            if (!IsConnected || _cam == null)
            {
                return Task.FromResult(new AcquisitionResult
                {
                    Success = false,
                    Message = "카메라가 연결되지 않았습니다."
                });
            }

            try
            {
                // Start grabbing if not already
                if (!_isGrabbing)
                {
                    int ret = _cam.MV_CC_StartGrabbing_NET();
                    if (ret != MyCamera.MV_OK)
                    {
                        return Task.FromResult(new AcquisitionResult
                        {
                            Success = false,
                            Message = $"StartGrabbing 실패: 0x{ret:X}"
                        });
                    }
                    _isGrabbing = true;
                }

                // Get one frame
                var frame = new MyCamera.MV_FRAME_OUT();
                int grabRet = _cam.MV_CC_GetImageBuffer_NET(ref frame, timeoutMs);
                if (grabRet != MyCamera.MV_OK)
                {
                    return Task.FromResult(new AcquisitionResult
                    {
                        Success = false,
                        Message = $"GetImageBuffer 실패: 0x{grabRet:X}"
                    });
                }

                try
                {
                    int w = frame.stFrameInfo.nWidth;
                    int h = frame.stFrameInfo.nHeight;

                    // Convert to BGR8 (OpenCV-friendly)
                    int dstSize = w * h * 3;
                    IntPtr dstBuf = Marshal.AllocHGlobal(dstSize);
                    try
                    {
                        var cvtParam = new MyCamera.MV_CC_PIXEL_CONVERT_PARAM
                        {
                            nWidth = (ushort)w,
                            nHeight = (ushort)h,
                            pSrcData = frame.pBufAddr,
                            nSrcDataLen = frame.stFrameInfo.nFrameLen,
                            enSrcPixelType = frame.stFrameInfo.enPixelType,
                            enDstPixelType = MyCamera.MvGvspPixelType.PixelType_Gvsp_BGR8_Packed,
                            pDstBuffer = dstBuf,
                            nDstBufferSize = (uint)dstSize
                        };
                        int cvtRet = _cam.MV_CC_ConvertPixelType_NET(ref cvtParam);
                        if (cvtRet != MyCamera.MV_OK)
                        {
                            return Task.FromResult(new AcquisitionResult
                            {
                                Success = false,
                                Message = $"PixelType 변환 실패: 0x{cvtRet:X}"
                            });
                        }

                        var mat = new Mat(h, w, MatType.CV_8UC3);
                        // dstBuf → mat.Data
                        unsafe
                        {
                            Buffer.MemoryCopy(dstBuf.ToPointer(), mat.Data.ToPointer(), dstSize, dstSize);
                        }

                        return Task.FromResult(new AcquisitionResult
                        {
                            Success = true,
                            Image2D = mat,
                            Message = $"Hikrobot 획득 완료 ({w}x{h})"
                        });
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(dstBuf);
                    }
                }
                finally
                {
                    _cam.MV_CC_FreeImageBuffer_NET(ref frame);
                }
            }
            catch (Exception ex)
            {
                return Task.FromResult(new AcquisitionResult
                {
                    Success = false,
                    Message = $"획득 오류: {ex.Message}"
                });
            }
        }

        public void StartProcessing()
        {
            if (_cam == null || _isGrabbing) return;
            int ret = _cam.MV_CC_StartGrabbing_NET();
            if (ret == MyCamera.MV_OK) _isGrabbing = true;
        }

        public void StopProcessing()
        {
            if (_cam == null || !_isGrabbing) return;
            _cam.MV_CC_StopGrabbing_NET();
            _isGrabbing = false;
        }

        private void ApplyCameraParameters()
        {
            if (_cam == null || _camera == null) return;
            try
            {
                // Exposure time (microseconds) — float node
                if (_camera.ExposureTime > 0)
                    _cam.MV_CC_SetFloatValue_NET("ExposureTime", (float)_camera.ExposureTime);

                // Gain — float node
                if (_camera.Gain > 0)
                    _cam.MV_CC_SetFloatValue_NET("Gain", (float)_camera.Gain);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hikrobot 파라미터 적용 실패: {ex.Message}");
            }
        }

        private static bool TryMatch(MyCamera.MV_CC_DEVICE_INFO dev, Models.CameraInfo target)
        {
            // GigE: IP 매칭
            if ((dev.nTLayerType & MyCamera.MV_GIGE_DEVICE) != 0 &&
                !string.IsNullOrWhiteSpace(target.ConnectionString))
            {
                var gigeInfo = (MyCamera.MV_GIGE_DEVICE_INFO)MyCamera.ByteToStruct(
                    dev.SpecialInfo.stGigEInfo, typeof(MyCamera.MV_GIGE_DEVICE_INFO))!;
                uint ipInt = gigeInfo.nCurrentIp;
                var ip = new IPAddress(new[]
                {
                    (byte)((ipInt >> 24) & 0xFF),
                    (byte)((ipInt >> 16) & 0xFF),
                    (byte)((ipInt >> 8) & 0xFF),
                    (byte)(ipInt & 0xFF)
                }).ToString();
                if (string.Equals(ip, target.ConnectionString, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // USB: SerialNumber 매칭
            if ((dev.nTLayerType & MyCamera.MV_USB_DEVICE) != 0 &&
                !string.IsNullOrWhiteSpace(target.SerialNumber))
            {
                var usbInfo = (MyCamera.MV_USB3_DEVICE_INFO)MyCamera.ByteToStruct(
                    dev.SpecialInfo.stUsb3VInfo, typeof(MyCamera.MV_USB3_DEVICE_INFO))!;
                var serial = usbInfo.chSerialNumber;
                if (string.Equals(serial, target.SerialNumber, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private void Cleanup()
        {
            try
            {
                if (_cam != null)
                {
                    if (_isGrabbing) _cam.MV_CC_StopGrabbing_NET();
                    _cam.MV_CC_CloseDevice_NET();
                    _cam.MV_CC_DestroyDevice_NET();
                    _cam = null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Hikrobot Cleanup 오류: {ex.Message}");
            }
            finally
            {
                _isGrabbing = false;
                IsConnected = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cleanup();
        }
    }
#endif
}
