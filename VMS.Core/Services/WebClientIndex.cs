namespace VMS.Core.Services
{
    /// <summary>
    /// Web 이 라인을 구분하는 번호(ClientIndex)의 허용 범위.
    ///
    /// <para><b>왜 상수로 두는가.</b> Web 은 등록·하트비트·결과 업로드에서 같은 범위를 강제한다
    /// (BODA.VMS.Web <c>Validators/Operations/ClientIndexRange.cs</c>). 예전에는 VMS 쪽 입력에
    /// 상한이 없어서, 100 이상으로 설정하면 하트비트는 통과하는데 자가 등록만 영구히 400 이 되어
    /// 라인이 등록되지 않은 채로 남고 그 사이 검사 결과가 전부 폐기됐다. 화면에는 "Web 연결 끊김"
    /// 만 보였다.</para>
    ///
    /// <para>범위를 바꿀 일이 생기면 <b>Web 쪽 상수와 함께</b> 바꾼다.</para>
    /// </summary>
    public static class WebClientIndex
    {
        public const int Min = 0;
        public const int Max = 99;

        public static bool IsValid(int value) => value >= Min && value <= Max;

        public const string RangeMessage = "라인 번호(Client Index)는 0~99 사이여야 합니다.";
    }
}
