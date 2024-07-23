using HandheldCompanion.Managers;
using HandheldCompanion.Utils;
using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Page = System.Windows.Controls.Page;

namespace HandheldCompanion.Views.QuickPages;

/// <summary>
///     Interaction logic for QuickHomePage.xaml
/// </summary>
public partial class QuickHomePage : Page
{

    public QuickHomePage(string Tag) : this()
    {
        this.Tag = Tag;

        HotkeysManager.HotkeyCreated += HotkeysManager_HotkeyCreated;
        HotkeysManager.HotkeyUpdated += HotkeysManager_HotkeyUpdated;

    }

    public QuickHomePage()
    {
        InitializeComponent();
    }

    private void HotkeysManager_HotkeyUpdated(Hotkey hotkey)
    {
        // UI thread
        Application.Current.Dispatcher.Invoke(UpdatePins);
    }

    private void HotkeysManager_HotkeyCreated(Hotkey hotkey)
    {
        // UI thread
        Application.Current.Dispatcher.Invoke(UpdatePins);
    }

    private void UpdatePins()
    {
        // todo, implement quick hotkey order
        QuickHotkeys.Children.Clear();

        foreach (var hotkey in HotkeysManager.Hotkeys.Values.Where(item => item.IsPinned))
            QuickHotkeys.Children.Add(hotkey.GetPin());
    }

}
