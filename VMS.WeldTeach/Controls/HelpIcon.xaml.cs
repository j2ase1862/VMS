using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace VMS.WeldTeach.Controls;

/// <summary>
/// 파란 물음표(?) 아이콘 — 마우스를 올리면 설명 팝업이 열리고, 아이콘/팝업을 벗어나면
/// 짧은 유예 후 닫힌다. 코드비하인드는 팝업 표시라는 뷰 전용 동작만 담당.
/// </summary>
public partial class HelpIcon : UserControl
{
    public static readonly DependencyProperty HelpTitleProperty =
        DependencyProperty.Register(nameof(HelpTitle), typeof(string), typeof(HelpIcon), new PropertyMetadata(""));

    public static readonly DependencyProperty HelpTextProperty =
        DependencyProperty.Register(nameof(HelpText), typeof(string), typeof(HelpIcon), new PropertyMetadata(""));

    public string HelpTitle
    {
        get => (string)GetValue(HelpTitleProperty);
        set => SetValue(HelpTitleProperty, value);
    }

    public string HelpText
    {
        get => (string)GetValue(HelpTextProperty);
        set => SetValue(HelpTextProperty, value);
    }

    private readonly DispatcherTimer _closeTimer;

    public HelpIcon()
    {
        InitializeComponent();
        _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _closeTimer.Tick += (_, _) => { _closeTimer.Stop(); HelpPopup.IsOpen = false; };
    }

    private void Icon_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _closeTimer.Stop();
        HelpPopup.IsOpen = true;
    }

    private void Icon_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => _closeTimer.Start();
}
