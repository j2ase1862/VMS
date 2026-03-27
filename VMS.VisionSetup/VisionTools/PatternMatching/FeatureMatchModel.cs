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

        internal List<FeatureMatchTool.EdgePoint> ModelEdges { get; set; } = new();
        internal float[]? ModelXArray { get; set; }
        internal float[]? ModelYArray { get; set; }
        internal int TemplateWidth { get; set; }
        internal int TemplateHeight { get; set; }
        internal double TrainedCenterX { get; set; }
        internal double TrainedCenterY { get; set; }

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
        }
    }
}
