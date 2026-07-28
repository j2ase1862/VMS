using System.Collections.Concurrent;
using System.Reflection;

namespace VMS.WeldTeach.Services;

/// <summary>
/// Occt.NET 래퍼 수명 가드.
/// 래퍼의 파이널라이저는 내부 참조(NoRelease) 객체까지 네이티브 delete 를 호출하는 결함이 있어,
/// GC 파이널라이저 스레드에서 힙 손상(0xC0000374)·행이 발생한다 (2026-07-28 스택 덤프로 확인).
/// 대응: 우리가 만드는 모든 OCCT 객체의 DeleteOnFinalize 를 false 로 꺼서 GC 경로의
/// 네이티브 해제를 전면 차단한다. 로드당 소량의 네이티브 메모리가 누수되지만(수백 KB~수 MB)
/// PoC 범위에서 허용 — 제품화 시 자체 래퍼로 교체하며 해소한다 (README 참조).
/// </summary>
internal static class OcctLifetime
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> Cache = new();

    /// <summary>OCCT 래퍼 객체의 파이널라이저 삭제를 끄고 그대로 돌려준다.</summary>
    public static T Keep<T>(T obj) where T : class?
    {
        if (obj is null) return obj!;
        var prop = Cache.GetOrAdd(obj.GetType(),
            static t => t.GetProperty("DeleteOnFinalize", BindingFlags.Public | BindingFlags.Instance));
        prop?.SetValue(obj, false);
        return obj;
    }
}
