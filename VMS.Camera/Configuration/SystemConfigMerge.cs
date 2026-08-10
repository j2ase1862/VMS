using System.Linq;
using System.Text.Json.Nodes;

namespace VMS.Camera.Configuration
{
    /// <summary>
    /// system_config.json 은 AppSetup / VMS / VisionSetup 이 서로 다른 모델로 읽고 쓴다.
    /// 각 앱이 자기 모델 전체를 재직렬화해 파일을 교체하면 상대 앱만 아는 키가 통째로
    /// 사라진다 — VMS 저장이 securityMode/webSso/robot* 를 지워 부팅 차단을 만들고,
    /// AppSetup 재저장이 카메라 steps 를 지워 노출/게인이 초기화되는 상호 파괴
    /// (현장 검증 2026-08-10; 카메라 레지스트리 동일 사고는 2026-07-22).
    ///
    /// 저장 직전 이 병합을 거치면 "내 모델이 아는 키는 내 값, 모르는 키는 기존 값 보존"이
    /// 보장된다. cameras 배열은 id 로 짝지어 entry 단위로 같은 규칙을 적용한다.
    /// </summary>
    public static class SystemConfigMerge
    {
        /// <summary>
        /// <paramref name="newRoot"/>(이번에 저장할 직렬화 결과)에 없는
        /// <paramref name="existingRoot"/>(디스크의 현재 파일)의 키를 보존한다.
        /// cameras 배열은 같은 id 의 entry 끼리 필드 단위로 보존하며,
        /// 새 목록에 없는 카메라 entry 는 삭제 의도로 보고 보존하지 않는다.
        /// </summary>
        public static void PreserveUnknown(JsonObject newRoot, JsonObject existingRoot)
        {
            foreach (var kv in existingRoot.ToList())
            {
                if (!newRoot.ContainsKey(kv.Key))
                    newRoot[kv.Key] = kv.Value?.DeepClone();
            }

            if (newRoot["cameras"] is not JsonArray newCameras ||
                existingRoot["cameras"] is not JsonArray oldCameras)
                return;

            foreach (var camera in newCameras.OfType<JsonObject>())
            {
                var id = (string?)camera["id"];
                if (id == null)
                    continue;

                var old = oldCameras.OfType<JsonObject>()
                    .FirstOrDefault(o => (string?)o["id"] == id);
                if (old == null)
                    continue;

                foreach (var kv in old.ToList())
                {
                    if (!camera.ContainsKey(kv.Key))
                        camera[kv.Key] = kv.Value?.DeepClone();
                }
            }
        }
    }
}
