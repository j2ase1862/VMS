using System.Text.Json;
using System.Windows.Media.Media3D;

namespace VMS.WeldTeach.Services;

/// <summary>
/// 핸드-아이 캘리브레이션 결과 행렬(T_cam2base) 파싱·검증 (그라인딩 스캔 명세 §S4-2·§8-5).
/// 캘리브레이션은 외부 모듈/도구 소관이고, 여기서는 그 결과를 4×4 행렬 파일로 받는다.
///
/// 허용 형식 (모두 <b>행 우선(row-major)</b>, 길이 단위 mm):
/// - JSON 2차원 배열: <c>[[r,r,r,tx],[r,r,r,ty],[r,r,r,tz],[0,0,0,1]]</c>
/// - JSON 평탄 배열(16개 또는 12개 — 마지막 행 생략 가능)
/// - JSON 객체: <c>tCam2BaseRowMajor</c> / <c>T_cam2base</c> / <c>matrix</c> 키 중 하나
///   (내보낸 경로 JSON 을 그대로 다시 넣어도 읽힌다)
/// - 공백/줄바꿈/쉼표로 구분된 숫자 12개 또는 16개 (txt)
/// </summary>
public static class HandEyeMatrix
{
    /// <summary>회전부 정규직교 허용 오차 — 캘리브레이션 결과의 수치 오차를 감안한 폭.</summary>
    private const double OrthoTolerance = 1e-3;

    /// <summary>
    /// 행렬 파일 텍스트를 파싱한다. 성공하면 WPF 규약(행벡터, p' = p·M)의 Matrix3D 를 돌려준다.
    /// 입력은 열벡터 규약(p' = T·p)의 행 우선 4×4 이므로 내부에서 전치해 담는다
    /// (내보내기의 MatrixRows 와 정확히 역연산).
    /// </summary>
    public static bool TryParse(string text, out Matrix3D matrix, out string error)
    {
        matrix = Matrix3D.Identity;
        error = string.Empty;

        double[]? v;
        try
        {
            v = ExtractNumbers(text);
        }
        catch (Exception ex)
        {
            error = $"파일을 읽을 수 없습니다: {ex.Message}";
            return false;
        }

        if (v == null)
        {
            error = "행렬을 찾지 못했습니다 — 4×4 행 우선 배열(또는 숫자 12·16개)이 필요합니다.";
            return false;
        }
        if (v.Length != 12 && v.Length != 16)
        {
            error = $"숫자 개수가 {v.Length}개입니다 — 12개(3×4) 또는 16개(4×4)여야 합니다.";
            return false;
        }
        foreach (var d in v)
            if (!double.IsFinite(d))
            {
                error = "행렬에 유한하지 않은 값(NaN/무한대)이 있습니다.";
                return false;
            }

        // 마지막 행이 있으면 (0,0,0,1) 인지 확인 — 강체 변환만 지원
        if (v.Length == 16 &&
            (Math.Abs(v[12]) > OrthoTolerance || Math.Abs(v[13]) > OrthoTolerance ||
             Math.Abs(v[14]) > OrthoTolerance || Math.Abs(v[15] - 1) > OrthoTolerance))
        {
            error = "마지막 행이 (0 0 0 1) 이 아닙니다 — 강체 변환(회전+평행이동)만 지원합니다.";
            return false;
        }

        // 회전부 정규직교 검사 (열벡터 규약의 3×3)
        var c0 = new Vector3D(v[0], v[4], v[8]);
        var c1 = new Vector3D(v[1], v[5], v[9]);
        var c2 = new Vector3D(v[2], v[6], v[10]);
        double n0 = c0.Length, n1 = c1.Length, n2 = c2.Length;
        if (Math.Abs(n0 - 1) > OrthoTolerance || Math.Abs(n1 - 1) > OrthoTolerance || Math.Abs(n2 - 1) > OrthoTolerance)
        {
            error = $"회전부가 정규직교가 아닙니다 (열 길이 {n0:F4}/{n1:F4}/{n2:F4}) — " +
                    "스케일이 섞였거나 단위(m↔mm)가 잘못됐을 수 있습니다.";
            return false;
        }
        if (Math.Abs(Vector3D.DotProduct(c0, c1)) > OrthoTolerance ||
            Math.Abs(Vector3D.DotProduct(c1, c2)) > OrthoTolerance ||
            Math.Abs(Vector3D.DotProduct(c2, c0)) > OrthoTolerance)
        {
            error = "회전부의 축이 서로 직교하지 않습니다.";
            return false;
        }
        double det = Vector3D.DotProduct(Vector3D.CrossProduct(c0, c1), c2);
        if (det < 0)
        {
            error = $"회전 행렬식이 음수입니다 ({det:F4}) — 좌우 반전(거울) 변환은 지원하지 않습니다.";
            return false;
        }

        // 열벡터 규약(행 우선 입력) → WPF 행벡터 규약으로 전치
        matrix = new Matrix3D(
            v[0], v[4], v[8], 0,
            v[1], v[5], v[9], 0,
            v[2], v[6], v[10], 0,
            v[3], v[7], v[11], 1);
        return true;
    }

    /// <summary>표시용 요약 — 평행이동과 회전각(축-각).</summary>
    public static string Describe(Matrix3D m)
    {
        double trace = m.M11 + m.M22 + m.M33;
        double angle = Math.Acos(Math.Clamp((trace - 1) / 2, -1, 1)) * 180 / Math.PI;
        return $"이동 ({m.OffsetX:F1}, {m.OffsetY:F1}, {m.OffsetZ:F1}) mm · 회전 {angle:F1}°";
    }

    /// <summary>JSON(배열·객체) 또는 공백 구분 텍스트에서 숫자열을 뽑는다.</summary>
    private static double[]? ExtractNumbers(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return null;

        if (text[0] == '[' || text[0] == '{')
        {
            using var doc = JsonDocument.Parse(text);
            var el = doc.RootElement;
            if (el.ValueKind == JsonValueKind.Object)
            {
                foreach (var key in new[] { "tCam2BaseRowMajor", "T_cam2base", "tCam2Base", "matrix" })
                    if (el.TryGetProperty(key, out var found) && found.ValueKind == JsonValueKind.Array)
                    {
                        el = found;
                        break;
                    }
                if (el.ValueKind != JsonValueKind.Array) return null;
            }
            var nums = new List<double>();
            Flatten(el, nums);
            return nums.Count > 0 ? nums.ToArray() : null;
        }

        var parts = text.Split(new[] { ' ', '\t', '\r', '\n', ',', ';' },
            StringSplitOptions.RemoveEmptyEntries);
        var vals = new List<double>(parts.Length);
        foreach (var p in parts)
        {
            if (!double.TryParse(p, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double d))
                return null;
            vals.Add(d);
        }
        return vals.Count > 0 ? vals.ToArray() : null;
    }

    private static void Flatten(JsonElement el, List<double> into)
    {
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in el.EnumerateArray()) Flatten(c, into);
        }
        else if (el.ValueKind == JsonValueKind.Number && el.TryGetDouble(out double d))
        {
            into.Add(d);
        }
    }
}
