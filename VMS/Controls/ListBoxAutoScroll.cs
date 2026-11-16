using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace VMS.Controls
{
    /// <summary>
    /// 목록 끝에 새 줄이 붙으면 따라 내려가는 ListBox 동작 (System Log 용, VM-03).
    ///
    /// <para>
    /// 사람이 위로 올려 읽고 있을 때는 끌어내리지 않는다 — 새 줄이 붙기 직전에 맨 아래에
    /// 있었을 때만 따라간다. 화면 조작만 하는 순수 UI 동작이라 ViewModel 이 아닌 여기에 둔다.
    /// </para>
    /// </summary>
    public static class ListBoxAutoScroll
    {
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled", typeof(bool), typeof(ListBoxAutoScroll),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        // ListBox 마다 붙인 구독을 기억해 두었다가 끌 때 뗀다
        private static readonly DependencyProperty SubscriptionProperty =
            DependencyProperty.RegisterAttached(
                "Subscription", typeof(NotifyCollectionChangedEventHandler), typeof(ListBoxAutoScroll));

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ListBox listBox) return;
            INotifyCollectionChanged items = listBox.Items;

            if (listBox.GetValue(SubscriptionProperty) is NotifyCollectionChangedEventHandler old)
            {
                items.CollectionChanged -= old;
                listBox.ClearValue(SubscriptionProperty);
            }

            if (e.NewValue is not true) return;

            NotifyCollectionChangedEventHandler handler = (_, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Add) return;

                // 붙기 직전 위치로 판단한다 — 레이아웃이 아직 새 줄을 반영하지 않은 시점이다
                var viewer = FindScrollViewer(listBox);
                if (viewer != null && !IsAtBottom(viewer.VerticalOffset, viewer.ScrollableHeight)) return;

                listBox.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    if (listBox.Items.Count == 0) return;
                    listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]);
                });
            };
            items.CollectionChanged += handler;
            listBox.SetValue(SubscriptionProperty, handler);
        }

        /// <summary>맨 아래(또는 스크롤할 것이 없는 상태)인가. 반 줄 정도의 오차는 맨 아래로 본다.</summary>
        internal static bool IsAtBottom(double verticalOffset, double scrollableHeight)
            => scrollableHeight <= 0 || verticalOffset >= scrollableHeight - 0.5;

        private static ScrollViewer? FindScrollViewer(DependencyObject root)
        {
            if (root is ScrollViewer sv) return sv;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var found = FindScrollViewer(VisualTreeHelper.GetChild(root, i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
