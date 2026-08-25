using System.Collections.Generic;
using System.Linq;
using VMS.Camera.Models;
using VMS.VisionSetup.Models;

namespace VMS.VisionSetup.Services
{
    /// <summary>
    /// 스텝 표시 이름 파생 규칙.
    /// 스텝 이름 "{카메라 표시 순번}-{스텝 순번}"의 카메라 순번은 이 PC의 카메라
    /// 레지스트리 등록 순서에 의존하는 값이므로, 레시피에 저장된 문자열을 신뢰하지 않고
    /// 로드/변경 시점마다 현재 레지스트리 기준으로 재계산한다.
    /// 이 PC에 등록되지 않은 카메라의 스텝은 "?-n"으로 표시해 소속을 드러낸다
    /// (다른 PC에서 작성된 레시피의 스텝 이름이 이 PC의 새 스텝과 겹치는 문제 방지).
    /// </summary>
    public static class StepNaming
    {
        /// <summary>
        /// 레시피의 모든 스텝 이름·순번을 카메라별로 재계산한다.
        /// 등록된 카메라: "{camIdx}-{n}", 미등록 카메라: "?-{n}", 카메라 미지정: "Step {n}".
        /// </summary>
        public static void RecomputeNames(Recipe recipe, IEnumerable<CameraInfo> cameras)
        {
            var cameraList = cameras as IList<CameraInfo> ?? cameras.ToList();

            foreach (var group in recipe.Steps.GroupBy(s => s.CameraId))
            {
                int camIdx = 0;
                for (int i = 0; i < cameraList.Count; i++)
                {
                    if (cameraList[i].Id == group.Key)
                    {
                        camIdx = i + 1;
                        break;
                    }
                }

                var steps = group.OrderBy(s => s.Sequence).ToList();
                for (int i = 0; i < steps.Count; i++)
                {
                    steps[i].Sequence = i + 1;
                    steps[i].Name = string.IsNullOrEmpty(group.Key)
                        ? $"Step {i + 1}"
                        : camIdx > 0 ? $"{camIdx}-{i + 1}" : $"?-{i + 1}";
                }
            }
        }

        /// <summary>
        /// 미등록 카메라를 참조하는 스텝의 CameraId 를 이 PC 의 카메라로 일괄 교체한다
        /// (다른 PC 에서 작성한 레시피 이식 — 재매핑 다이얼로그의 적용 본체).
        /// mapping: 구 CameraId → 이 PC 의 CameraInfo.Id. 반환값은 교체된 스텝 수.
        /// 서로 다른 구 카메라를 같은 카메라로 합쳐도 RecomputeNames 가 순번을 재부여한다.
        /// </summary>
        public static int RemapCameras(Recipe recipe, IReadOnlyDictionary<string, string> mapping)
        {
            int remapped = 0;
            foreach (var step in recipe.Steps)
            {
                if (!string.IsNullOrEmpty(step.CameraId) &&
                    mapping.TryGetValue(step.CameraId, out var newId) &&
                    newId != step.CameraId)
                {
                    step.CameraId = newId;
                    remapped++;
                }
            }
            return remapped;
        }

        /// <summary>
        /// 레시피가 참조하지만 현재 카메라 레지스트리에 없는 카메라 ID 목록.
        /// </summary>
        public static List<string> GetUnregisteredCameraIds(Recipe recipe, IEnumerable<CameraInfo> cameras)
        {
            var registeredIds = cameras.Select(c => c.Id).ToHashSet();
            return recipe.Steps
                .Select(s => s.CameraId)
                .Where(id => !string.IsNullOrEmpty(id) && !registeredIds.Contains(id))
                .Distinct()
                .ToList();
        }
    }
}
