namespace VMS.VisionSetup.Demo
{
    /// <summary>
    /// 홍보 데모용: 3D 포인트클라우드 뷰의 턴테이블 자동 회전을 시작/정지하라는 메시지.
    /// View 전용 동작(카메라 제어)이므로 ViewModel 이 아닌 MainView 코드비하인드가 처리한다.
    /// </summary>
    public sealed class Demo3DOrbitMessage
    {
        public bool Start { get; }
        public Demo3DOrbitMessage(bool start) => Start = start;
    }
}
