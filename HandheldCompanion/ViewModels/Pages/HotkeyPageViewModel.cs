﻿using HandheldCompanion.Extensions;
using HandheldCompanion.Inputs;
using HandheldCompanion.Managers;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;

namespace HandheldCompanion.ViewModels
{
    public class HotkeyPageViewModel : BaseViewModel
    {
        public ObservableCollection<HotkeyViewModel> HotkeysList { get; set; } = [];
        public ICommand CreateHotkeyCommand { get; private set; }

        public HotkeyPageViewModel()
        {
            HotkeysManager.Updated += HotkeysManager_Updated;
            HotkeysManager.Deleted += HotkeysManager_Deleted;
            HotkeysManager.Initialized += HotkeysManager_Initialized;

            InputsManager.StartedListening += InputsManager_StartedListening;
            InputsManager.StoppedListening += InputsManager_StoppedListening;

            ControllerManager.ControllerSelected += ControllerManager_ControllerSelected;

            CreateHotkeyCommand = new DelegateCommand(async () =>
            {
                HotkeysManager.UpdateOrCreateHotkey(new Hotkey());
            });
        }

        private void ControllerManager_ControllerSelected(Controllers.IController Controller)
        {
            // (re)draw chords on controller update
            foreach (HotkeyViewModel hotkeyViewModel in HotkeysList)
                hotkeyViewModel.DrawChords();
        }

        private void HotkeysManager_Initialized()
        {
            foreach (Hotkey hotkey in HotkeysManager.GetHotkeys())
                HotkeysManager_Updated(hotkey);
        }

        private void HotkeysManager_Updated(Hotkey hotkey)
        {
            if (hotkey.IsInternal)
                return;

            HotkeyViewModel? foundHotkey = HotkeysList.ToList().FirstOrDefault(p => p.Hotkey.ButtonFlags == hotkey.ButtonFlags);
            if (foundHotkey is null)
            {
                HotkeysList.SafeAdd(new HotkeyViewModel(hotkey));
            }
            else
            {
                foundHotkey.Hotkey = hotkey;
            }
        }

        private void HotkeysManager_Deleted(Hotkey hotkey)
        {
            HotkeyViewModel? foundHotkey = HotkeysList.ToList().FirstOrDefault(p => p.Hotkey.ButtonFlags == hotkey.ButtonFlags);
            if (foundHotkey is not null)
            {
                HotkeysList.SafeRemove(foundHotkey);
                foundHotkey.Dispose();
            }
        }

        private void InputsManager_StartedListening(ButtonFlags buttonFlags, InputsChordTarget chordTarget)
        {
            HotkeyViewModel hotkeyViewModel = HotkeysList.Where(h => h.Hotkey.ButtonFlags == buttonFlags).FirstOrDefault();
            if (hotkeyViewModel != null)
                hotkeyViewModel.SetListening(true, chordTarget);
        }

        private void InputsManager_StoppedListening(ButtonFlags buttonFlags, InputsChord storedChord)
        {
            HotkeyViewModel hotkeyViewModel = HotkeysList.Where(h => h.Hotkey.ButtonFlags == buttonFlags).FirstOrDefault();
            if (hotkeyViewModel != null)
                hotkeyViewModel.SetListening(false, storedChord.chordTarget);
        }
    }
}