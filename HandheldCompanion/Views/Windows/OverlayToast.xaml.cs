using HandheldCompanion.Views.Classes;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HandheldCompanion.Views.Windows;

public enum ToastIcons
{
    VolumeUp,
    VolumeDown,
    Volume,
    VolumeMute,
    BrightnessUp,
    BrightnessDown,
    Nightlight,
    NightlightOff,
    BacklightUp,
    BacklightDown,
    Game,
    Touchscreen,
    Touchpad,
    Microphone,
    MicrophoneMute,
    FnLock,
    Battery,
    BatteryFullyCharged,
    Charger,
    Controller
}

/// <summary>
/// Interaction logic for OverlayToast.xaml
/// </summary>
public partial class OverlayToast : OverlayWindow
{

    protected static string toastTitle = "Balanced";
    protected static string toastText = "Toast text";
    protected static ToastIcons? toastIcon = null;
    private readonly DispatcherTimer dispatcher = new(
        DispatcherPriority.Normal,
        Dispatcher.CurrentDispatcher)
    {
        IsEnabled = false,
        Interval = TimeSpan.FromMilliseconds(2850)
    };
    public OverlayToast()
    {
        InitializeComponent();
        dispatcher.Tick += dispatcherElapsed;
        ShowInTaskbar = false;
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
    }

    public void RunToast(string text, ToastIcons? icon = null)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            dispatcher.Stop();
            toastText = text;
            toastIcon = icon;
            ToastText.Text = toastText;
            ToastIcon.Glyph = toastIcon switch
            {
                ToastIcons.Game => "\ue7fc",
                ToastIcons.Touchscreen => "\ueda4",
                ToastIcons.Touchpad => "\uefa5",
                ToastIcons.BrightnessUp => "\ue706",
                ToastIcons.BrightnessDown => "\uec8a",
                ToastIcons.Charger => "\ue83e",
                ToastIcons.Battery => "\ue859",
                ToastIcons.BatteryFullyCharged => "\uebb5",
                ToastIcons.VolumeUp => "\ue995",
                ToastIcons.VolumeDown => "\ue994",
                ToastIcons.VolumeMute => "\ue74f",
                ToastIcons.Volume => "\ue767",
                ToastIcons.MicrophoneMute => "\uf781",
                ToastIcons.Microphone => "\ue720",
                ToastIcons.Nightlight => "\uf08c",
                ToastIcons.NightlightOff => "\uE706",
                _ => "\ue713"
            };
            ToastPanel.Visibility = Visibility.Visible;
            Show();
            dispatcher.Start();
        });
    }

    private void dispatcherElapsed(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            dispatcher.Stop();
            ToastPanel.Visibility = Visibility.Hidden;
            await Task.Delay(150);
            Hide();
        });
    }
}
