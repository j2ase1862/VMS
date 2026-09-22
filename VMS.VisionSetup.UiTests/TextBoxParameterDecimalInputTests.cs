using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VMS.VisionSetup.Controls;
using Xunit;

namespace VMS.VisionSetup.Tests
{
    /// <summary>
    /// TextBoxParameter 에 소수를 타이핑할 수 있는지 검증 —
    /// 현장 보고(2026-09-22): Geometry 도구의 Expected Value 에 소수점을 입력할 수 없다.
    ///
    /// <para>원인은 값의 왕복이었다. TextBox.Text ↔ Value(object) ↔ 뷰모델의 double 이
    /// 키 입력마다 오갔기 때문에, "23." 을 치면 double 23 이 되고 그 23 이 글자로 되돌아와
    /// "23" 으로 덮였다. 소수점이 사라지니 이어서 "5" 를 치면 <b>"235"</b> 가 된다 —
    /// 입력이 안 되는 정도가 아니라 10배 틀린 값이 조용히 저장됐다.</para>
    /// </summary>
    [Collection(WpfUiCollection.Name)]
    public class TextBoxParameterDecimalInputTests
    {
        private readonly WpfUiFixture _ui;
        public TextBoxParameterDecimalInputTests(WpfUiFixture ui) => _ui = ui;

        private sealed class Vm : ObservableObject
        {
            private double _expectedValue = 23;
            public double ExpectedValue
            {
                get => _expectedValue;
                // 실제 GeometryToolSettingsViewModel 과 같은 모양 — 세터가 알림을 쏜다
                set { _expectedValue = value; OnPropertyChanged(); }
            }
        }

        private static TextBox FindTextBox(DependencyObject root)
        {
            if (root is TextBox tb) return tb;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var found = FindTextBox(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null!;
        }

        /// <summary>컨트롤을 렌더 트리에 올려 템플릿을 적용한다 (창은 띄우지 않는다).</summary>
        private static (TextBox box, Vm vm) Build()
        {
            var vm = new Vm();
            var param = new TextBoxParameter { Label = "Expected Value" };
            param.SetBinding(TextBoxParameter.ValueProperty,
                new Binding(nameof(Vm.ExpectedValue)) { Source = vm, Mode = BindingMode.TwoWay });

            var host = new Border { Child = param };
            host.Measure(new Size(400, 200));
            host.Arrange(new Rect(0, 0, 400, 200));
            host.UpdateLayout();

            var box = FindTextBox(param);
            Assert.NotNull(box);
            return (box, vm);
        }

        /// <summary>사람이 한 글자씩 치는 것과 같게 — 캐럿 끝에서 한 자씩 추가.</summary>
        private static void Type(TextBox box, string text)
        {
            foreach (var ch in text)
                box.Text += ch;
        }

        [Fact]
        public void 소수점을_이어서_입력할_수_있다()
        {
            _ui.Run(() =>
            {
                var (box, vm) = Build();
                box.Text = string.Empty;

                Type(box, "23.5");

                Assert.Equal("23.5", box.Text);
                Assert.Equal(23.5, vm.ExpectedValue, 6);
            });
        }

        [Fact]
        public void 소수점_직후에도_글자가_지워지지_않는다()
        {
            _ui.Run(() =>
            {
                var (box, _) = Build();
                box.Text = string.Empty;

                Type(box, "23.");

                // "23" 으로 되돌아가면 그 뒤 숫자를 이어 칠 수 없다.
                Assert.Equal("23.", box.Text);
            });
        }

        [Fact]
        public void 바깥에서_값이_바뀌면_표시도_따라간다()
        {
            // 입력 중 덮어쓰기를 막는 것이 "값이 바뀌어도 표시가 안 바뀐다" 가 되면 안 된다.
            _ui.Run(() =>
            {
                var (box, vm) = Build();
                box.Text = "23.5";
                Assert.Equal(23.5, vm.ExpectedValue, 6);

                vm.ExpectedValue = 99.25;

                Assert.Equal("99.25", box.Text);
            });
        }

        [Fact]
        public void 값을_지우고_다시_칠_수_있다()
        {
            _ui.Run(() =>
            {
                var (box, vm) = Build();
                box.Text = string.Empty;
                Assert.Equal(string.Empty, box.Text);   // 지운 글자가 되살아나면 재입력이 안 된다

                Type(box, "1.25");

                Assert.Equal("1.25", box.Text);
                Assert.Equal(1.25, vm.ExpectedValue, 6);
            });
        }

        [Fact]
        public void 음수_소수도_입력된다()
        {
            _ui.Run(() =>
            {
                var (box, vm) = Build();
                box.Text = string.Empty;

                Type(box, "-0.75");

                Assert.Equal("-0.75", box.Text);
                Assert.Equal(-0.75, vm.ExpectedValue, 6);
            });
        }
    }
}
