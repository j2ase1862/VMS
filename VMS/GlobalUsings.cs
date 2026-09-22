// 레시피 스키마의 정의처는 VMS.VisionSetup.Models 하나다.
//
// 예전에는 VMS 가 같은 이름의 축소 사본(VMS.Models.Recipe/InspectionStep/ToolConfig)을
// 따로 들고 있었고, 그 사본에 없는 필드는 레시피를 읽는 순간 조용히 사라졌다 —
// InspectionStep.Resolution 이 빠져 mm 판정이 "변환 불가"로 전부 NG 가 되고,
// ToolConfig.ROIAngle 이 빠져 회전 ROI 가 0° 로 실행되고, VMS 에서 저장하면
// calibration·criteria·webRecipeId 가 파일에서 지워졌다 (2026-09-22 현장).
//
// 네임스페이스를 통째로 여는 대신 alias 를 쓰는 이유: VMS.Models 에도 같은 이름의
// ToolResultItem·PlcResultMapping 이 있다. 그 둘은 레시피에 직렬화되지 않는 런타임
// 전용 타입이라 VMS 쪽을 그대로 쓴다.
global using InspectionStep = VMS.VisionSetup.Models.InspectionStep;
global using PassFailCriteria = VMS.VisionSetup.Models.PassFailCriteria;
global using Recipe = VMS.VisionSetup.Models.Recipe;
global using RecipeInfo = VMS.VisionSetup.Models.RecipeInfo;
global using ToolConfig = VMS.VisionSetup.Models.ToolConfig;
global using ToolConnectionConfig = VMS.VisionSetup.Models.ToolConnectionConfig;
