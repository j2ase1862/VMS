using VMS.WeldTeach.Models;

namespace VMS.WeldTeach.Interfaces;

/// <summary>
/// CAD 커널 추상화 — 구현체(OCCT/Eyeshot 등)를 교체할 수 있도록 뷰모델은 이 인터페이스만 사용한다.
/// </summary>
public interface ICadKernelService
{
    /// <summary>STEP 파일을 로드해 렌더링 메쉬와 엣지 위상 데이터를 만든다.</summary>
    CadModelData LoadStep(string path);

    /// <summary>T-필릿 용접 시편(STEP)을 생성해 파일 경로를 돌려준다. CAD 파일이 없어도 PoC 를 시험할 수 있게 한다.</summary>
    string GenerateSampleStep(string outputPath);
}
