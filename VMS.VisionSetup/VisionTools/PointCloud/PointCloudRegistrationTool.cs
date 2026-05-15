using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using VMS.Camera.Models;
using VMS.Camera.Utils;
using VMS.VisionSetup.Models;
using VMS.VisionSetup.Services;

namespace VMS.VisionSetup.VisionTools.PointCloud
{
    /// <summary>
    /// 3D 점군 정합 도구 — ICP(Iterative Closest Point) 알고리즘.
    /// Reference 점군(.vpc 파일) ↔ Source 점군(VisionService.CurrentPointCloud) 정합.
    /// 결과 4x4 변환 행렬을 source에 적용해 정합된 점군을 VisionService.CurrentPointCloud로 갱신.
    /// </summary>
    public class PointCloudRegistrationTool : VisionToolBase
    {
        // ── Reference 점군 (.vpc 파일 경로) ──
        private string _referencePath = string.Empty;
        public string ReferencePath
        {
            get => _referencePath;
            set => SetProperty(ref _referencePath, value ?? string.Empty);
        }

        // ── ICP 파라미터 ──
        private int _maxIterations = 50;
        public int MaxIterations
        {
            get => _maxIterations;
            set => SetProperty(ref _maxIterations, Math.Clamp(value, 1, 500));
        }

        private float _tolerance = 0.01f;
        /// <summary>수렴 임계 — 반복 간 변환 변화량(mm)이 이보다 작아지면 종료.</summary>
        public float Tolerance
        {
            get => _tolerance;
            set => SetProperty(ref _tolerance, Math.Clamp(value, 0.0001f, 10.0f));
        }

        private bool _applyTransformToSource = true;
        /// <summary>true면 source에 변환 행렬을 적용해 VisionService.CurrentPointCloud 갱신. false면 행렬만 산출.</summary>
        public bool ApplyTransformToSource
        {
            get => _applyTransformToSource;
            set => SetProperty(ref _applyTransformToSource, value);
        }

        public bool IsReferenceLoaded => !string.IsNullOrEmpty(ReferencePath) && File.Exists(ReferencePath);

        public PointCloudRegistrationTool()
        {
            Name = "PointCloud Registration";
            ToolType = "PointCloudRegistrationTool";
        }

        /// <summary>
        /// 현재 VisionService.CurrentPointCloud를 Reference로 저장.
        /// 호출자가 파일 경로 결정 (다이얼로그 등).
        /// </summary>
        public bool SaveCurrentAsReference(string filePath)
        {
            var src = VisionService.Instance.CurrentPointCloud;
            if (src == null || src.PointCount == 0) return false;
            if (string.IsNullOrEmpty(filePath)) return false;
            try
            {
                src.SaveToFile(filePath);
                ReferencePath = filePath;
                OnPropertyChanged(nameof(IsReferenceLoaded));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public override VisionResult Execute(Mat inputImage)
        {
            var result = new VisionResult();
            var sw = Stopwatch.StartNew();

            try
            {
                var src = VisionService.Instance.CurrentPointCloud;
                if (src == null || src.PointCount == 0)
                {
                    result.Success = false;
                    result.Message = "No source point cloud available.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                if (!IsReferenceLoaded)
                {
                    result.Success = false;
                    result.Message = "Reference (.vpc) not loaded. Use 'Save Current as Reference' first.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                PointCloudData reference;
                try
                {
                    reference = PointCloudData.LoadFromFile(ReferencePath);
                }
                catch (Exception ex)
                {
                    result.Success = false;
                    result.Message = $"Failed to load reference: {ex.Message}";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                if (reference.PointCount == 0)
                {
                    result.Success = false;
                    result.Message = "Reference point cloud is empty.";
                    result.OutputImage = inputImage.Clone();
                    return result;
                }

                // ICP 실행 → 변환 행렬
                var transform = TransformUtils.ICP(reference, src, MaxIterations, Tolerance);

                // Source에 적용
                if (ApplyTransformToSource)
                {
                    var aligned = TransformUtils.TransformPointCloud(src, transform);
                    VisionService.Instance.CurrentPointCloud = aligned;
                }

                // 결과 행렬을 키별로 노출 (4x4 = 16개)
                result.Data["RefPoints"] = reference.PointCount;
                result.Data["SrcPoints"] = src.PointCount;
                result.Data["M11"] = transform.M11; result.Data["M12"] = transform.M12; result.Data["M13"] = transform.M13; result.Data["M14"] = transform.M14;
                result.Data["M21"] = transform.M21; result.Data["M22"] = transform.M22; result.Data["M23"] = transform.M23; result.Data["M24"] = transform.M24;
                result.Data["M31"] = transform.M31; result.Data["M32"] = transform.M32; result.Data["M33"] = transform.M33; result.Data["M34"] = transform.M34;
                result.Data["M41"] = transform.M41; result.Data["M42"] = transform.M42; result.Data["M43"] = transform.M43; result.Data["M44"] = transform.M44;

                // 편의: 변환의 평행이동 + 회전 추정값
                result.Data["TranslationX"] = transform.M41;
                result.Data["TranslationY"] = transform.M42;
                result.Data["TranslationZ"] = transform.M43;
                result.Data["TranslationNorm"] = Math.Sqrt(
                    transform.M41 * transform.M41 +
                    transform.M42 * transform.M42 +
                    transform.M43 * transform.M43);

                result.OutputImage = inputImage.Clone();
                result.Success = true;
                result.Message = $"ICP aligned: |Δt|={result.Data["TranslationNorm"]:F3}mm "
                    + $"(Ref={reference.PointCount}, Src={src.PointCount}, MaxIter={MaxIterations})";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Registration failed: {ex.Message}";
            }
            finally
            {
                sw.Stop();
                ExecutionTime = sw.Elapsed.TotalMilliseconds;
                LastResult = result;
            }
            return result;
        }

        public override List<string> GetAvailableResultKeys()
        {
            return new List<string>
            {
                "Success", "RefPoints", "SrcPoints",
                "TranslationX", "TranslationY", "TranslationZ", "TranslationNorm",
                "M11", "M12", "M13", "M14",
                "M21", "M22", "M23", "M24",
                "M31", "M32", "M33", "M34",
                "M41", "M42", "M43", "M44"
            };
        }

        public override VisionToolBase Clone()
        {
            var clone = new PointCloudRegistrationTool
            {
                Name = this.Name,
                ToolType = this.ToolType,
                IsEnabled = this.IsEnabled,
                ReferencePath = this.ReferencePath,
                MaxIterations = this.MaxIterations,
                Tolerance = this.Tolerance,
                ApplyTransformToSource = this.ApplyTransformToSource
            };
            CopyPlcMappingsTo(clone);
            return clone;
        }
    }
}
