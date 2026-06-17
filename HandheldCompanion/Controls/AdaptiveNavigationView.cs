using iNKORE.UI.WPF.Modern.Controls;
using iNKORE.UI.WPF.Modern.Controls.Primitives;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace HandheldCompanion.Controls
{
    /// <summary>
    /// Custom NavigationView that switches items to icon-only mode when width is insufficient,
    /// instead of showing an overflow menu.
    /// </summary>
    public class AdaptiveNavigationView : NavigationView
    {
        private const double TopNavModeHysteresisWidth = 12.0;
        private const string TopNavAreaPartName = "TopNavArea";
        private const string TopNavBorderPartName = "TopNavBorder";
        private const string TopNavGridPartName = "TopNavGrid";
        private const string TopNavMenuItemsHostPartName = "TopNavMenuItemsHost";
        private const string TopNavOverflowButtonPartName = "TopNavOverflowButton";

        public static readonly DependencyProperty TopNavItemsAlignmentProperty =
            DependencyProperty.Register(
                nameof(TopNavItemsAlignment),
                typeof(HorizontalAlignment),
                typeof(AdaptiveNavigationView),
                new PropertyMetadata(HorizontalAlignment.Left, OnTopNavItemsAlignmentChanged));

        public HorizontalAlignment TopNavItemsAlignment
        {
            get => (HorizontalAlignment)GetValue(TopNavItemsAlignmentProperty);
            set => SetValue(TopNavItemsAlignmentProperty, value);
        }

        private Border? _topNavBorder;
        private Grid? _topNavGrid;
        private FrameworkElement? _topNavArea;
        private ItemsRepeater m_topNavRepeater;
        private Button m_topNavOverflowButton;
        private bool _isTopNavIconOnly;
        private double _cachedExpandedTopNavWidth;

        public AdaptiveNavigationView()
        {
        }

        private static void OnTopNavItemsAlignmentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is AdaptiveNavigationView navigationView)
            {
                navigationView._cachedExpandedTopNavWidth = 0.0;
                navigationView.ApplyTopNavAlignment();
                navigationView.InvalidateMeasure();
            }
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _topNavArea = GetTemplateChild(TopNavAreaPartName) as FrameworkElement;
            _topNavBorder = GetTemplateChild(TopNavBorderPartName) as Border;
            _topNavGrid = GetTemplateChild(TopNavGridPartName) as Grid;
            m_topNavRepeater = GetTemplateChild(TopNavMenuItemsHostPartName) as ItemsRepeater;
            m_topNavOverflowButton = GetTemplateChild(TopNavOverflowButtonPartName) as Button;
            _cachedExpandedTopNavWidth = 0.0;

            ApplyTopNavAlignment();
            SyncRealizedTopNavState(true);

            if (m_topNavRepeater is not null)
            {
                m_topNavRepeater.ElementPrepared += TopNavRepeater_ElementPrepared;
                m_topNavRepeater.ElementClearing += TopNavRepeater_ElementClearing;
            }
        }

        private static DependencyPropertyDescriptor? _iconPropertyDescriptor;
        private static DependencyPropertyDescriptor IconPropertyDescriptor =>
            _iconPropertyDescriptor ??= DependencyPropertyDescriptor.FromName(
                "Icon", typeof(NavigationViewItem), typeof(NavigationViewItem));

        private void TopNavRepeater_ElementPrepared(ItemsRepeater sender, ItemsRepeaterElementPreparedEventArgs args)
        {
            if (args.Element is NavigationViewItem navItem)
                IconPropertyDescriptor.AddValueChanged(navItem, NavItem_IconChanged);

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => SyncRealizedTopNavState(true));
        }

        private void TopNavRepeater_ElementClearing(ItemsRepeater sender, ItemsRepeaterElementClearingEventArgs args)
        {
            if (args.Element is NavigationViewItem navItem)
                IconPropertyDescriptor.RemoveValueChanged(navItem, NavItem_IconChanged);
        }

        private void NavItem_IconChanged(object? sender, EventArgs e)
        {
            // Icon binding updated on a realized item — re-apply the correct visual state.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => SyncRealizedTopNavState(true));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            if (IsTopNavigationEnabled())
            {
                ApplyTopNavAlignment();
                UpdateTopNavigationPresentation(availableSize);
            }

            Size desiredSize = base.MeasureOverride(availableSize);

            SyncRealizedTopNavState(false);

            return desiredSize;
        }

        protected override Size ArrangeOverride(Size arrangeBounds)
        {
            Size arrangedSize = base.ArrangeOverride(arrangeBounds);

            SyncRealizedTopNavState(false);

            return arrangedSize;
        }

        private bool IsTopNavigationEnabled()
        {
            return PaneDisplayMode == NavigationViewPaneDisplayMode.Top && _topNavGrid is not null && m_topNavRepeater is not null;
        }

        private void UpdateTopNavigationPresentation(Size availableSize)
        {
            if (_topNavGrid is null)
                return;

            if (double.IsInfinity(availableSize.Width) || availableSize.Width <= 0)
            {
                SetTopNavIconOnly(false);
                return;
            }

            if (_isTopNavIconOnly)
            {
                ApplyTopNavItemPresentation(true, false);

                if (_cachedExpandedTopNavWidth > 0.0 && availableSize.Width >= _cachedExpandedTopNavWidth + TopNavModeHysteresisWidth)
                {
                    SetTopNavIconOnly(false);
                }

                return;
            }

            ApplyTopNavItemPresentation(false, false);

            double expandedWidth = MeasureCurrentTopNavigationWidth();
            if (expandedWidth > 0.0)
            {
                _cachedExpandedTopNavWidth = expandedWidth;
            }

            if (expandedWidth > availableSize.Width)
            {
                SetTopNavIconOnly(true);
            }
        }

        private double MeasureCurrentTopNavigationWidth()
        {
            if (_topNavGrid is null)
                return 0.0;

            SuppressOverflowButton();
            _topNavGrid.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return _topNavGrid.DesiredSize.Width;
        }

        private void SetTopNavIconOnly(bool isIconOnly)
        {
            if (_isTopNavIconOnly == isIconOnly)
            {
                ApplyTopNavItemPresentation(isIconOnly, false);
                return;
            }

            _isTopNavIconOnly = isIconOnly;
            ApplyTopNavItemPresentation(isIconOnly, true);
        }

        private void ApplyTopNavItemPresentation(bool isIconOnly, bool updateLayout)
        {
            if (m_topNavRepeater is null)
                return;

            foreach (NavigationViewItem item in EnumerateDescendants<NavigationViewItem>(m_topNavRepeater))
            {
                NavigationViewItemPresenter? presenter = FindDescendant<NavigationViewItemPresenter>(item);
                if (presenter is null)
                    continue;

                string stateName;
                if (item.Icon is null)
                    stateName = "ContentOnly";
                else
                    stateName = isIconOnly ? "IconOnly" : "IconOnLeft";

                VisualStateManager.GoToState(presenter, stateName, false);

                // Subscribe/unsubscribe the hook that re-applies IconOnly after the presenter's
                // own hover/focus state update resets it back to IconOnLeft.
                item.MouseEnter -= NavItem_ReapplyIconOnlyState;
                item.MouseLeave -= NavItem_ReapplyIconOnlyState;
                item.GotKeyboardFocus -= NavItem_ReapplyIconOnlyState;
                item.LostKeyboardFocus -= NavItem_ReapplyIconOnlyState;
                item.PreviewMouseLeftButtonDown -= NavItem_ReapplyIconOnlyState;
                item.PreviewMouseLeftButtonUp -= NavItem_ReapplyIconOnlyState;

                if (isIconOnly && item.Icon is not null)
                {
                    item.MouseEnter += NavItem_ReapplyIconOnlyState;
                    item.MouseLeave += NavItem_ReapplyIconOnlyState;
                    item.GotKeyboardFocus += NavItem_ReapplyIconOnlyState;
                    item.LostKeyboardFocus += NavItem_ReapplyIconOnlyState;
                    item.PreviewMouseLeftButtonDown += NavItem_ReapplyIconOnlyState;
                    item.PreviewMouseLeftButtonUp += NavItem_ReapplyIconOnlyState;
                }

                if (updateLayout)
                {
                    presenter.InvalidateMeasure();
                    presenter.InvalidateArrange();
                }
            }
        }

        private void NavItem_ReapplyIconOnlyState(object sender, RoutedEventArgs e)
        {
            if (sender is not NavigationViewItem item)
                return;

            // Defer so our GoToState runs after the presenter's own UpdateVisualState,
            // which fires synchronously during the same event. Normal priority runs
            // before the render pass, preventing any visible flash.
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                NavigationViewItemPresenter? presenter = FindDescendant<NavigationViewItemPresenter>(item);
                if (presenter is not null)
                    VisualStateManager.GoToState(presenter, "IconOnly", false);
            });
        }

        private void ApplyTopNavAlignment()
        {
            if (_topNavArea is null || _topNavBorder is null || _topNavGrid is null || m_topNavRepeater is null)
                return;

            _topNavArea.HorizontalAlignment = HorizontalAlignment.Stretch;
            _topNavBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
            _topNavGrid.HorizontalAlignment = HorizontalAlignment.Stretch;
            m_topNavRepeater.HorizontalAlignment = TopNavItemsAlignment;

            if (_topNavGrid.ColumnDefinitions.Count <= 5)
                return;

            _topNavGrid.ColumnDefinitions[3].Width = TopNavItemsAlignment == HorizontalAlignment.Left
                ? GridLength.Auto
                : new GridLength(1, GridUnitType.Star);

            _topNavGrid.ColumnDefinitions[5].Width = GridLength.Auto;
        }

        private void SuppressOverflowButton()
        {
            if (m_topNavOverflowButton is null)
                return;

            m_topNavOverflowButton.Visibility = Visibility.Collapsed;
            m_topNavOverflowButton.IsEnabled = false;
        }

        private void SyncRealizedTopNavState(bool updateLayout)
        {
            if (!IsTopNavigationEnabled())
                return;

            SuppressOverflowButton();
            ApplyTopNavItemPresentation(_isTopNavIconOnly, updateLayout);
        }

        private static T? FindDescendant<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root is null)
                return null;

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                {
                    return match;
                }

                T? nestedMatch = FindDescendant<T>(child);
                if (nestedMatch is not null)
                    return nestedMatch;
            }

            return null;
        }

        private static System.Collections.Generic.IEnumerable<T> EnumerateDescendants<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root is null)
                yield break;

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                {
                    yield return match;
                }

                foreach (T nestedMatch in EnumerateDescendants<T>(child))
                {
                    yield return nestedMatch;
                }
            }
        }
    }
}
