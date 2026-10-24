# 매뉴얼 6장 실행 예 · §5 조작 그림용 VisionSetup 장면 목록 생성 → D:/Temp/mancap_scenes/scenes.json
#   레시피는 D:/GS_TestData 의 recipe_*.json 을 D:/Temp/mancap_scenes/recipes 로 복사해 쓴다(동결본 무변경).
#   실행: $env:VMS_CAPTURE_SCENES=<json>; VMS.VisionSetup.exe(Debug) --instance mancap --capture-scenes <출력 폴더>
#   자르기: crop_tool_scenes.py <출력 폴더> · crop_manual_figs.py <VMS --capture-inspect 폴더> <출력 폴더>
import json
R = "D:/Temp/mancap_scenes/recipes/"
G = "D:/GS_TestData/"
def sc(name, recipe, img, tool, step=0, tab=None, pre=None, post="selected", show_roi=False):
    d = dict(name=name, recipe=R + recipe, step=step, image=G + img, tool=tool, post=post, showRoi=show_roi)
    if tab: d["tab"] = tab
    if pre: d["pre"] = [dict(step=s, image=G + i) for s, i in pre]
    return d
PP = "recipe_GS_Preprocess.json"; PPI = "Preprocess/images/PP01_reference.png"
MS = "recipe_GS_Measure.json"; MSI = "Measure/images/MS01_ok_nominal.png"
D3 = "recipe_GS_3D.json"; P3 = "3D/pointclouds/P3D03_ok_rot_minus44.vpc"; P3N = "3D/pointclouds/P3D09_ng_height_synth.vpc"
S = [
 sc("Blur", PP, PPI, 1), sc("Morphology", PP, PPI, 5), sc("ImageEnhance", PP, PPI, 3),
 sc("PolarUnwrap", "recipe_GS_PolarUnwrap.json", "PolarUnwrap/images/PU02_ok_rotated_35deg.png", 0),
 sc("Grayscale", PP, PPI, 0), sc("Threshold", PP, PPI, 4), sc("EdgeDetection", PP, PPI, 6), sc("Histogram", PP, "Preprocess/images/PP02_low_contrast.png", 2),
 sc("FeatureMatch", "recipe_GS_PatternMatch_Fitting.json", "PatternMatch/images/PM06_multi_2ea.png", 1, step=0),
 sc("ShapeMatch", "recipe_GS_PatternMatch_Fitting.json", "PatternMatch/images/PM02_rotate.png", 1, step=1),
 sc("MatchAlign", "recipe_GS_MatchAlign.json", "Align/MatchAlign/images/AL03_rotate_33.png", 2),
 sc("MultiStepAlign", "recipe_GS_MultiStepAlign.json", "Align/MultiStepAlign/images/MS03_rotate_3deg_B.png", 2, step=1,
    pre=[(0, "Align/MultiStepAlign/images/MS03_rotate_3deg_A.png")]),
 sc("Blob", "recipe_GS_Blob.json", "Blob/images/BL01_ok_6ea.png", 0),
 sc("Caliper", MS, MSI, 0), sc("LineFit", MS, MSI, 3), sc("CircleFit", MS, MSI, 1), sc("Geometry", MS, "Measure/images/MS04_ng_hole_pitch.png", 5),
 sc("OCR", "recipe_GS_OCR.json", "OCR/images/OC01_ok.png", 0),
 sc("OCV", "recipe_GS_OCV.json", "OCV/images/OV04_ng_missing_char.png", 0),
 sc("CodeReader", "recipe_GS_DataMatrix.json", "DataMatrix/images/DM02_gs1.png", 0, step=1),
 sc("PCFilter", D3, P3, 3, step=1, tab="2D Image"), sc("PCRegistration", D3, P3, 4, step=1, tab="2D Image"),
 sc("PCMaskCrop", D3, P3, 1, tab="2D Image"), sc("PCCluster", D3, P3, 2, tab="2D Image"),
 sc("PCDeviation", D3, "3D/pointclouds/P3D11_ng_twist_synth.vpc", 5, step=1, tab="2D Image"),
 sc("HeightSlicer", D3, P3, 0, tab="2D Image"), sc("PlaneFit", D3, P3, 4, tab="2D Image"), sc("Geometry3D", D3, P3N, 5, tab="2D Image"),
 sc("PCLineFit", "recipe_GS_3D_LineFit.json", "3D/LineFit/pointclouds/LF04_ng_bent_8mm.vpc", 2, tab="2D Image"),
 sc("PointCloudView", D3, P3, -1, tab="Point Cloud"),
 sc("Detection", "recipe_GS_DL_Detection.json", "DeepLearning/Detection/images/DET01_A_TEST_01.png", 0),
 sc("Segmentation", "recipe_GS_DL_SemanticSeg.json", "DeepLearning/SemanticSeg/images/SEM01_A_TEST_01.png", 0),
 sc("RfdetrSeg", "recipe_GS_DL_InstanceSeg.json", "DeepLearning/InstanceSeg/images/INS01_A_TEST_01.png", 0),
 sc("Classify", "recipe_GS_DL_Classification.json", "DeepLearning/Classification/images/CLS19_NG_tape_07.png", 0),
 sc("Anomaly", "recipe_GS_DL_Anomaly.json", "DeepLearning/Anomaly/images/ANO15_defect_pen_07.png", 0),
 sc("Result", "recipe_GS_Result.json", "Result/images/MS05_ng_block_angle.png", 7, post="all"),
 sc("Ensemble", "recipe_GS_DL_Ensemble.json", "DeepLearning/Ensemble/images/ENS03_defect_pen_09.png", 2, post="all"),
 sc("ColorExtract", "recipe_GS_ColorExtract.json", "ColorExtract/images/CE02_ok_shift.png", 0),
 sc("ColorMatch", "recipe_GS_Color.json", "Color/images/CL02_ok_shift.png", 0),
 sc("ImageRectify", "recipe_GS_ImageRectify.json", "ImageRectify/images/MS01_ok_nominal.png", 0),
 sc("PhotometricStereo", "recipe_GS_PhotometricStereo.json", "PhotometricStereo/images/NG/PS_NG_L0_az000.png", 0, step=1),
 sc("RoiEdit", MS, MSI, 0, post="none", show_roi=True),
 sc("StepCameraSettings", "recipe_GS_Blob.json", "Blob/images/BL01_ok_6ea.png", -1),
]
json.dump(S, open("D:/Temp/mancap_scenes/scenes.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
print(len(S))
