# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# From the solution root: D:\projects\BODA-AI\BODA-AI\BODA VISION AI
dotnet build "BODA VISION AI/BODA VISION AI.csproj"
dotnet run --project "BODA VISION AI/BODA VISION AI.csproj"
```

No test projects exist in this solution.

## Project Overview

OpenCvSharp-based machine vision system replacing Cognex VisionPro. Users build image processing pipelines by dragging vision tools into a workspace, connecting them, and running them on loaded images. Korean language is used throughout UI strings and comments.

- **Framework**: .NET 8.0 WPF (`net8.0-windows7.0`), WinExe
- **Architecture**: MVVM using CommunityToolkit.Mvvm 8.4.0
- **Vision Library**: OpenCvSharp4 4.11.0
- **Namespace**: `BODA_VISION_AI`

## Architecture

### Data Flow

1. User loads image → `MainViewModel.OpenImageFile()` → stored as `VisionService.CurrentImage` (OpenCV `Mat`)
2. User drags tool from palette → added to `VisionService.Tools` collection
3. User configures tool connections (Image/Coordinates/Result types via `ToolConnection`)
4. Run pipeline → `VisionService` executes tools sequentially, each receiving input from connected source or original image
5. Results stored in `VisionService.Results`, displayed via `ImageCanvas` control

### Key Patterns

**VisionService** is a singleton (`VisionService.Instance`) that owns the tool execution queue, results collection, and tool interconnections.

**All vision tools** inherit from `VisionToolBase` (which extends `ObservableObject`):
- Must implement `Execute(Mat inputImage)` → returns `VisionResult`
- Must implement `Clone()` for UI drag-and-drop
- Each tool has a unique `Id` (GUID), `Name`, `ToolType`, `ROI` support, and `IsEnabled` flag
- ROI handling via `GetROIImage()`, `GetAdjustedROI()`, `ApplyROIResult()` in the base class

**Adding a new vision tool**: Create a class in the appropriate `VisionTools/` subfolder, inherit `VisionToolBase`, implement `Execute()` and `Clone()`, define parameters as `[ObservableProperty]` fields.

**Commands** use `RelayCommand` / `RelayCommand<T>` from CommunityToolkit.Mvvm.

**ImageCanvas** is a custom WPF control handling image display (Mat → WriteableBitmap), interactive ROI drawing/editing, and overlay graphics rendering.

### Tool Connection System

Tools connect via `ToolConnection` with three types:
- **Image**: Output image from source → input to target
- **Coordinates**: Detected point/region data passed between tools
- **Result**: Success/failure status propagation

### Vision Tools by Category

| Category | Tools |
|----------|-------|
| ImageProcessing | GrayscaleTool, BlurTool, ThresholdTool, EdgeDetectionTool, MorphologyTool, HistogramTool |
| PatternMatching | FeatureMatchTool |
| BlobAnalysis | BlobTool |
| Measurement | CaliperTool, LineFitTool, CircleFitTool |

Each maps to a Cognex VisionPro equivalent (e.g., FeatureMatchTool → CogPMAlignTool PatMax, BlobTool → CogBlobTool).
