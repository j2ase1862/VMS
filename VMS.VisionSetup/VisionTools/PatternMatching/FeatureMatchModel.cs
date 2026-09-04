using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace VMS.VisionSetup.VisionTools.PatternMatching
{
    /// <summary>
    /// Encapsulates per-model data for multi-model FeatureMatchTool.
    /// Each model holds its own trained template, edge points, bin tables, and native pose buffers.
    /// </summary>
    public unsafe class FeatureMatchModel : ObservableObject, IDisposable
    {
        private string _name = "Model 1";
        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        private Mat? _templateImage;
        public Mat? TemplateImage
        {
            get => _templateImage;
            set => SetProperty(ref _templateImage, value);
        }

        private Mat? _trainedFeatureImage;
        public Mat? TrainedFeatureImage
        {
            get => _trainedFeatureImage;
            set => SetProperty(ref _trainedFeatureImage, value);
        }

        // 학습 마스크 (don't-care) — 템플릿과 같은 크기의 8UC1, 255=학습 제외.
        // 그림자·가변 각인·정반사 등 제외 영역이 본체 윤곽에 붙는 경우까지 다루기 위해
        // 셰이프 목록이 아닌 비트맵 — 마스크 편집기(브러시/사각형/지우개)로 칠한다.
        private Mat? _trainMask;
        public Mat? TrainMask
        {
            get => _trainMask;
            set
            {
                var old = _trainMask;
                if (SetProperty(ref _trainMask, value))
                {
                    old?.Dispose();
                    OnPropertyChanged(nameof(HasTrainMask));
                }
            }
        }

        public bool HasTrainMask => _trainMask != null && !_trainMask.Empty();

        internal List<FeatureMatchTool.EdgePoint> ModelEdges { get; set; } = new();
        internal float[]? ModelXArray { get; set; }
        internal float[]? ModelYArray { get; set; }
        internal int TemplateWidth { get; set; }
        internal int TemplateHeight { get; set; }
        internal double TrainedCenterX { get; set; }
        internal double TrainedCenterY { get; set; }

        /// <summary>
        /// 학습 ROI 각도(°, 캔버스 규약). 회전 ROI 는 정렬 워프 후 학습되므로 같은 장면을
        /// 매칭하면 Angle 이 이 값으로 나온다 — Match Align 학습 기준의 기준 각도 (2026-09-04).
        /// 축 정렬 ROI / 전체 이미지 학습이면 0.
        /// </summary>
        internal double TrainedAngle { get; set; }

        /// <summary>
        /// 구 레시피(학습 원점 미저장) 복원 표식 — 역직렬화 시 템플릿 재학습이 기본 속성(ROI) 적용보다
        /// 먼저 돌아 원점이 템플릿 중심(w/2,h/2)으로 남는다. ApplyBaseProperties 뒤에 ROI 기준으로 재계산.
        /// </summary>
        internal bool NeedsLegacyOriginFix { get; set; }

        internal const int NUM_GRAD_BINS = 36;
        internal List<int>[]? GradBinTable { get; set; }
        internal int[]? BinOffsets { get; set; }
        internal int[]? BinIndices { get; set; }

        // Pre-allocated unmanaged buffers for pose evaluation
        internal int* NativeRxBuf;
        internal int* NativeRyBuf;
        internal float* NativeRdxBuf;
        internal float* NativeRdyBuf;
        internal int* NativeMarginBuf;
        internal double* NativeAngleBuf;
        internal double* NativeScaleBuf;
        internal int PoseBufferCapacity;
        internal int PoseModelN;

        public bool IsTrained => ModelEdges.Count >= 10;

        internal void EnsurePoseBufferCapacity(int requiredPoses, int modelPoints)
        {
            if (requiredPoses <= PoseBufferCapacity && modelPoints == PoseModelN)
                return;

            FreeNativePoseBuffers();

            nuint totalElements = (nuint)requiredPoses * (nuint)modelPoints;
            NativeRxBuf = (int*)NativeMemory.AlignedAlloc(totalElements * (nuint)sizeof(int), 32);
            NativeRyBuf = (int*)NativeMemory.AlignedAlloc(totalElements * (nuint)sizeof(int), 32);
            NativeRdxBuf = (float*)NativeMemory.AlignedAlloc(totalElements * (nuint)sizeof(float), 32);
            NativeRdyBuf = (float*)NativeMemory.AlignedAlloc(totalElements * (nuint)sizeof(float), 32);
            NativeMarginBuf = (int*)NativeMemory.AlignedAlloc((nuint)requiredPoses * (nuint)sizeof(int), 32);
            NativeAngleBuf = (double*)NativeMemory.AlignedAlloc((nuint)requiredPoses * (nuint)sizeof(double), 32);
            NativeScaleBuf = (double*)NativeMemory.AlignedAlloc((nuint)requiredPoses * (nuint)sizeof(double), 32);
            PoseBufferCapacity = requiredPoses;
            PoseModelN = modelPoints;
        }

        internal void FreeNativePoseBuffers()
        {
            if (NativeRxBuf != null) { NativeMemory.AlignedFree(NativeRxBuf); NativeRxBuf = null; }
            if (NativeRyBuf != null) { NativeMemory.AlignedFree(NativeRyBuf); NativeRyBuf = null; }
            if (NativeRdxBuf != null) { NativeMemory.AlignedFree(NativeRdxBuf); NativeRdxBuf = null; }
            if (NativeRdyBuf != null) { NativeMemory.AlignedFree(NativeRdyBuf); NativeRdyBuf = null; }
            if (NativeMarginBuf != null) { NativeMemory.AlignedFree(NativeMarginBuf); NativeMarginBuf = null; }
            if (NativeAngleBuf != null) { NativeMemory.AlignedFree(NativeAngleBuf); NativeAngleBuf = null; }
            if (NativeScaleBuf != null) { NativeMemory.AlignedFree(NativeScaleBuf); NativeScaleBuf = null; }
            PoseBufferCapacity = 0;
            PoseModelN = 0;
        }

        public void Dispose()
        {
            FreeNativePoseBuffers();
            _templateImage?.Dispose();
            _templateImage = null;
            _trainedFeatureImage?.Dispose();
            _trainedFeatureImage = null;
            _trainMask?.Dispose();
            _trainMask = null;
        }
    }
}
