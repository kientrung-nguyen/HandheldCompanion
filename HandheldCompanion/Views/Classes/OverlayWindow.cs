using HandheldCompanion.Functions;
using HandheldCompanion.Helpers;
using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using static HandheldCompanion.WinAPI;

namespace HandheldCompanion.Views.Classes;

public class OverlayWindow : Window
{
    public HorizontalAlignment _HorizontalAlignment;
    public VerticalAlignment _VerticalAlignment;


    public OverlayWindow()
    {
        // overlay specific settings
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        FocusManager.SetIsFocusScope(this, false);

        SizeChanged += (o, e) => UpdatePosition();

        Loaded += OverlayWindow_Loaded;
        IsVisibleChanged += OverlayWindow_IsVisibleChanged;
    }

    private void OverlayWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // TODO, IMPLEMENT ME
    }

    public new HorizontalAlignment HorizontalAlignment
    {
        get => _HorizontalAlignment;

        set
        {
            if (_HorizontalAlignment != value)
            {
                _HorizontalAlignment = value;
                UpdatePosition();
            }
        }
    }

    public new VerticalAlignment VerticalAlignment
    {
        get => _VerticalAlignment;

        set
        {
            if (_VerticalAlignment != value)
            {
                _VerticalAlignment = value;
                UpdatePosition();
            }
        }
    }

    private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var source = PresentationSource.FromVisual(this) as HwndSource;
        source?.AddHook(WndProc);

        //Set the window style to noactivate.
        var interopHelper = new WindowInteropHelper(this);
        SetWindowLong(interopHelper.Handle, (int)GETWINDOWLONG.GWL_EXSTYLE,
            (int)(GetWindowLong(interopHelper.Handle, (int)GETWINDOWLONG.GWL_EXSTYLE) | (uint)WINDOWSTYLE.WS_EX_NOACTIVATE)
            );
    }

    private nint WndProc(nint hWnd, int msg, nint wparam, nint lparam, ref bool handled)
    {
        if (msg == (int)WINDOWMESSAGE.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr((uint)MOUSEACTIVATE.MA_NOACTIVATE);
        }
        else
            return IntPtr.Zero;
    }

    private void UpdatePosition()
    {
        var r = SystemParameters.WorkArea;

        switch (HorizontalAlignment)
        {
            case HorizontalAlignment.Left:
                Left = 0;
                break;

            default:
            case HorizontalAlignment.Center:
                Left = r.Width / 2 - Width / 2;
                break;

            case HorizontalAlignment.Right:
                Left = r.Right - Width;
                break;

            case HorizontalAlignment.Stretch:
                Left = 0;
                Width = SystemParameters.PrimaryScreenWidth;
                break;
        }

        switch (VerticalAlignment)
        {
            case VerticalAlignment.Top:
                Top = 0;
                break;

            default:
            case VerticalAlignment.Center:
                Top = r.Height / 2 - Height / 2;
                break;

            case VerticalAlignment.Bottom:
                Top = r.Height - Height;
                break;

            case VerticalAlignment.Stretch:
                Top = 0;
                Height = SystemParameters.PrimaryScreenHeight;
                break;
        }
    }

    public void SetVisibility(Visibility visibility)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            this.Visibility = visibility;
        });
    }

    public virtual void ToggleVisibility()
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            switch (Visibility)
            {
                case Visibility.Visible:
                    Hide();
                    break;
                case Visibility.Collapsed:
                case Visibility.Hidden:
                    try { Show(); } catch { /* ItemsRepeater might have a NaN DesiredSize */ }
                    break;
            }
        });
    }

    //#region import

    //private const int GWL_EXSTYLE = -20;
    //private const int WS_EX_NOACTIVATE = 0x08000000;

    //[DllImport("user32.dll")]
    //public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    //[DllImport("user32.dll")]
    //public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    //#endregion
}