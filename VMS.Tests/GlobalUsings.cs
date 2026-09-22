// 레시피 스키마는 VMS.VisionSetup.Models 가 단일 정의처 — VMS/GlobalUsings.cs 와 같은 alias.
// (전역 using 은 프로젝트 경계를 넘지 않으므로 테스트 프로젝트에도 같은 선언이 필요하다.)
global using InspectionStep = VMS.VisionSetup.Models.InspectionStep;
global using PassFailCriteria = VMS.VisionSetup.Models.PassFailCriteria;
global using Recipe = VMS.VisionSetup.Models.Recipe;
global using RecipeInfo = VMS.VisionSetup.Models.RecipeInfo;
global using ToolConfig = VMS.VisionSetup.Models.ToolConfig;
global using ToolConnectionConfig = VMS.VisionSetup.Models.ToolConnectionConfig;
