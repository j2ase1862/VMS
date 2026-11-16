using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using VMS.Controls;
using Xunit;

namespace VMS.Tests.ViewModels
{
    /// <summary>
    /// 화면 동작 붙임(attached behavior) 시험 — WPF 컨트롤은 STA 스레드에서 만든다.
    /// </summary>
    public class UiBehaviorTests
    {
        private static void RunSta(Action body)
        {
            Exception? error = null;
            var t = new Thread(() =>
            {
                try { body(); }
                catch (Exception ex) { error = ex; }
            });
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            t.Join();
            if (error != null) throw new Xunit.Sdk.XunitException(error.ToString());
        }

        [Fact]
        public void DatePickerIsoFormat_ShowsYyyyMmDd_RegardlessOfCulture()
        {
            // VM-17 — 영어(미국) 문화권이어도 글 칸은 2026-11-06
            RunSta(() =>
            {
                var picker = new DatePicker { Language = System.Windows.Markup.XmlLanguage.GetLanguage("en-US") };
                DatePickerIsoFormat.SetIsEnabled(picker, true);
                var window = new Window { Content = picker, Width = 300, Height = 100, ShowActivated = false };
                window.Measure(new Size(300, 100));
                window.Arrange(new Rect(0, 0, 300, 100));
                picker.ApplyTemplate();

                picker.SelectedDate = new DateTime(2026, 11, 6);

                var box = (DatePickerTextBox)picker.Template.FindName("PART_TextBox", picker);
                Assert.Equal("2026-11-06", box.Text);
                window.Close();
            });
        }

        [Fact]
        public void ListBoxAutoScroll_AtBottomCheck()
        {
            // VM-03 — 맨 아래(또는 스크롤할 것이 없음)일 때만 따라간다
            Assert.True(ListBoxAutoScroll.IsAtBottom(0, 0));
            Assert.True(ListBoxAutoScroll.IsAtBottom(10, 10));
            Assert.True(ListBoxAutoScroll.IsAtBottom(9.7, 10));
            Assert.False(ListBoxAutoScroll.IsAtBottom(5, 10));
        }

        [Fact]
        public void ToolResultItem_ShowsPassFail()
        {
            // VM-04 — P/F 칸은 Pass / Fail
            var item = new VMS.Models.ToolResultItem { Result = true };
            Assert.Equal("Pass", item.ResultText);
            string? changed = null;
            item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.ResultText)) changed = e.PropertyName; };
            item.Result = false;
            Assert.Equal("Fail", item.ResultText);
            Assert.Equal(nameof(item.ResultText), changed);
        }
    }
}
