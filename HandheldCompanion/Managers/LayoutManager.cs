using HandheldCompanion.Actions;
using HandheldCompanion.Controllers;
using HandheldCompanion.Inputs;
using HandheldCompanion.Managers.Desktop;
using HandheldCompanion.Misc;
using HandheldCompanion.Shared;
using HandheldCompanion.Utils;
using HandheldCompanion.Views;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;

namespace HandheldCompanion.Managers;

internal static class LayoutManager
{
    public static List<LayoutTemplate> Templates =
    [
        LayoutTemplate.DefaultLayout,
        LayoutTemplate.DesktopLayout,
        LayoutTemplate.NintendoLayout,
        LayoutTemplate.KeyboardLayout,
        LayoutTemplate.GamepadMouseLayout,
        LayoutTemplate.GamepadJoystickLayout
    ];

    private static object updateLock = new();
    private static Layout currentLayout = new();
    private static ScreenRotation currentOrientation = new();
    private static Layout profileLayout;
    private static Layout desktopLayout;
    private static readonly string desktopLayoutFile = "desktop";

    public static string LayoutsPath;
    public static string TemplatesPath;

    public static FileSystemWatcher layoutWatcher { get; set; }

    public static bool IsInitialized;

    static LayoutManager()
    {
        // initialiaze path
        LayoutsPath = Path.Combine(MainWindow.SettingsPath, "layouts");
        if (!Directory.Exists(LayoutsPath))
            Directory.CreateDirectory(LayoutsPath);

        TemplatesPath = Path.Combine(MainWindow.SettingsPath, "templates");
        if (!Directory.Exists(TemplatesPath))
            Directory.CreateDirectory(TemplatesPath);

        // monitor layout files
        layoutWatcher = new FileSystemWatcher
        {
            Path = TemplatesPath,
            EnableRaisingEvents = true,
            IncludeSubdirectories = true,
            Filter = "*.json",
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
        };
    }

    public static async Task Start()
    {
        if (IsInitialized)
            return;

        // process community templates
        var fileEntries = Directory.GetFiles(TemplatesPath, "*.json", SearchOption.AllDirectories);
        foreach (var fileName in fileEntries)
            ProcessLayoutTemplate(fileName);

        foreach (var layoutTemplate in Templates)
            Updated?.Invoke(layoutTemplate);

        var desktopFile = Path.Combine(LayoutsPath, $"{desktopLayoutFile}.json");
        desktopLayout = ProcessLayout(desktopFile);
        if (desktopLayout is null)
        {
            desktopLayout = LayoutTemplate.DesktopLayout.Layout.Clone() as Layout;
            DesktopLayout_Updated(desktopLayout);
        }

        // manage desktop layout events
        desktopLayout.Updated += DesktopLayout_Updated;

        // manage layout watcher events
        layoutWatcher.Created += LayoutWatcher_Template;
        layoutWatcher.Changed += LayoutWatcher_Template;

        // manage events
        ProfileManager.Applied += ProfileManager_Applied;
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;
        MultimediaManager.DisplayOrientationChanged += MultimediaManager_DisplayOrientationChanged;

        // raise events
        if (ProfileManager.IsInitialized)
        {
            ProfileManager_Applied(ProfileManager.GetCurrent(), UpdateSource.Background);
        }

        if (MultimediaManager.IsInitialized)
        {
            MultimediaManager_DisplayOrientationChanged(MultimediaManager.GetScreenOrientation());
        }

        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", "LayoutManager");
        return;
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        base.PrepareStop();

        // manage desktop layout events
        desktopLayout.Updated -= DesktopLayout_Updated;

        // manage layout watcher events
        layoutWatcher.Created -= LayoutWatcher_Template;
        layoutWatcher.Changed -= LayoutWatcher_Template;

        // manage events
        ProfileManager.Applied -= ProfileManager_Applied;
        SettingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
        MultimediaManager.DisplayOrientationChanged -= MultimediaManager_DisplayOrientationChanged;

        IsInitialized = false;
		base.Stop();
        LogManager.LogInformation("{0} has stopped", "LayoutManager");
    }

    // this event is called from non main thread and it creates LayoutTemplate which is a WPF element
    private static void LayoutWatcher_Template(object sender, FileSystemEventArgs e)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            ProcessLayoutTemplate(e.FullPath);
        });
    }

    private static Layout? ProcessLayout(string fileName)
    {
        Layout layout = null;

        try
        {
            string outputraw = File.ReadAllText(fileName);
            layout = JsonConvert.DeserializeObject<Layout>(outputraw, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            });
        }
        catch (Exception ex)
        {
            LogManager.LogError("Could not parse Layout {0}. {1}", fileName, ex.Message);
        }

        // failed to parse
        if (layout is null)
            LogManager.LogError("Could not parse Layout {0}", fileName);

        return layout;
    }

    private static void ProcessLayoutTemplate(string fileName)
    {
        LayoutTemplate layoutTemplate = null;

        try
        {
            string outputraw = File.ReadAllText(fileName);
            layoutTemplate = JsonConvert.DeserializeObject<LayoutTemplate>(outputraw, new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            });
        }
        catch (Exception ex)
        {
            LogManager.LogError("Could not parse LayoutTemplate {0}. {1}", fileName, ex.Message);
        }

        // failed to parse
        if (layoutTemplate is null || layoutTemplate.Layout is null)
        {
            LogManager.LogError("Could not parse LayoutTemplate {0}", fileName);
            return;
        }

        // todo: implement deduplication
        Templates.Add(layoutTemplate);
        Updated?.Invoke(layoutTemplate);
    }

    private static void DesktopLayout_Updated(Layout layout)
    {
        SerializeLayout(layout, desktopLayoutFile);
    }

    private static void ProfileManager_Applied(Profile profile, UpdateSource source)
    {
        SetProfileLayout(profile);
    }

    private static void SetProfileLayout(Profile profile = null)
    {
        var defaultProfile = ProfileManager.GetDefault();

        if (profile.LayoutEnabled)
            // use profile layout if enabled
            profileLayout = profile.Layout.Clone() as Layout;
        else if (defaultProfile.LayoutEnabled)
            // fallback to default profile layout if enabled
            profileLayout = defaultProfile.Layout.Clone() as Layout;
        else
            // this should not happen, defaultProfile LayoutEnabled should always be true 
            profileLayout = null;

        // only update current layout if we're not into desktop layout mode
        if (!SettingsManager.Get<bool>("DesktopLayoutEnabled"))
            SetActiveLayout(profileLayout);
    }

    public static Layout GetCurrent()
    {
        return currentLayout;
    }

    public static Layout GetDesktop()
    {
        return desktopLayout;
    }

    public static void SerializeLayout(Layout layout, string fileName)
    {
        var jsonString = JsonConvert.SerializeObject(layout, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All
        });

        fileName = Path.Combine(LayoutsPath, $"{fileName}.json");
        if (FileUtils.IsFileWritable(fileName))
            File.WriteAllText(fileName, jsonString);
    }

    public static void SerializeLayoutTemplate(LayoutTemplate layoutTemplate)
    {
        var jsonString = JsonConvert.SerializeObject(layoutTemplate, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All
        });

        string fileName = Path.Combine(TemplatesPath, $"{layoutTemplate.Name}_{layoutTemplate.Author}.json");
        if (FileUtils.IsFileWritable(fileName))
            File.WriteAllText(fileName, jsonString);
    }

    private static void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
    {
        switch (name)
        {
            case "DesktopProfileOnStart":
                {
                    if (!SettingsManager.IsInitialized)
                        SettingsManager.Set("DesktopLayoutEnabled", value, false);
                }
                break;

            case "DesktopLayoutEnabled":
                {
                    switch (Convert.ToBoolean(value))
                    {
                        case true:
                            SetActiveLayout(desktopLayout);
                            break;
                        case false:
                            SetActiveLayout(profileLayout);
                            break;
                    }
                }
                break;
        }
    }

    private static void MultimediaManager_DisplayOrientationChanged(ScreenRotation rotation)
    {
        currentOrientation = rotation;

        // apply orientation
        UpdateOrientation();
    }

    private static void UpdateOrientation()
    {
        if (currentLayout is null)
            return;

        foreach (var axisLayout in currentLayout.AxisLayout)
        {
            // pull action
            var action = axisLayout.Value;

            if (action is null)
                continue;

            if (action.AutoRotate)
                action.SetOrientation(currentOrientation);
        }
    }

    private static async void SetActiveLayout(Layout layout)
    {
        lock (updateLock)
        {
            currentLayout = layout;

            // (re)apply orientation
            UpdateOrientation();
        }
    }

    private static ControllerState outputState = new();
    public static ControllerState MapController(ControllerState controllerState)
    {
        // when no profile active and default is disabled, do 1:1 controller mapping
        if (currentLayout is null)
            return controllerState;

        lock (updateLock)
        {
            // clean output state, there should be no leaking of current controller state,
            // only buttons/axes mapped from the layout should be passed on
            // according to ChatGPT, (re)initializing ConcurrentDictionary is faster than clearing it
            outputState.ButtonState.State = new();
            outputState.AxisState.State = new();
            outputState.GyroState = new(controllerState.GyroState.Accelerometer, controllerState.GyroState.Gyroscope);

            // we need to check for shifter(s) first
            ShiftSlot shiftSlot = ShiftSlot.None;
            foreach (KeyValuePair<ButtonFlags, bool> buttonState in controllerState.ButtonState.State)
            {
                ButtonFlags button = buttonState.Key;
                bool value = buttonState.Value;

                // skip, if not mapped
                if (!currentLayout.ButtonLayout.TryGetValue(button, out List<IActions> actions))
                    continue;

                foreach (IActions action in actions)
                {
                    switch (action.actionType)
                    {
                        // button to shift
                        case ActionType.Shift:
                            {
                                ShiftActions sAction = action as ShiftActions;
                                sAction.Execute(button, value, shiftSlot);
                                bool outVal = sAction.GetValue();

                                if (outVal) shiftSlot |= sAction.ShiftSlot;
                            }
                            break;
                    }
                }
            }

            foreach (KeyValuePair<ButtonFlags, bool> buttonState in controllerState.ButtonState.State)
            {
                ButtonFlags button = buttonState.Key;
                bool value = buttonState.Value;

                // skip, if not mapped
                if (!currentLayout.ButtonLayout.TryGetValue(button, out List<IActions> actions))
                    continue;

                foreach (IActions action in actions)
                {
                    switch (action.actionType)
                    {
                        // button to button
                        case ActionType.Button:
                            {
                                ButtonActions bAction = action as ButtonActions;
                                bAction.Execute(button, value, shiftSlot);

                                bool outVal = bAction.GetValue() || outputState.ButtonState[bAction.Button];
                                outputState.ButtonState[bAction.Button] = outVal;
                            }
                            break;

                        // button to keyboard key
                        case ActionType.Keyboard:
                            {
                                KeyboardActions kAction = action as KeyboardActions;
                                kAction.Execute(button, value, shiftSlot);
                            }
                            break;

                        // button to mouse click
                        case ActionType.Mouse:
                            {
                                MouseActions mAction = action as MouseActions;
                                mAction.Execute(button, value, shiftSlot);
                            }
                            break;
                    }

                    switch (action.actionState)
                    {
                        case ActionState.Aborted:
                        case ActionState.Stopped:
                            {
                                foreach (IActions action2 in actions.Where(a => a.ShiftSlot == action.ShiftSlot))
                                {
                                    if (action2 == action)
                                        continue;

                                    if (!action2.Interruptable)
                                        continue;

                                    if (action2.actionState == ActionState.Succeed)
                                        continue;

                                    if (action2.actionState != ActionState.Stopped && action2.actionState != ActionState.Aborted)
                                        action2.actionState = ActionState.Stopped;
                                }

                                if (action.actionState == ActionState.Aborted)
                                {
                                    int idx = actions.IndexOf(action);
                                    if (idx >= 0 && idx < actions.Count - 1)
                                    {
                                        var currentShiftSlot = action.ShiftSlot;

                                        // Find the next action with the same ShiftSlot after the current index
                                        IActions nextAction = actions
                                            .Skip(idx + 1)
                                            .FirstOrDefault(a => a.ShiftSlot == currentShiftSlot && a.Interruptable);

                                        if (nextAction != null)
                                            nextAction.actionState = ActionState.Forced;
                                    }
                                }
                            }
                            break;

                        case ActionState.Running:
                            {
                                foreach (IActions action2 in actions.Where(a => a.ShiftSlot == action.ShiftSlot))
                                {
                                    if (action2 == action)
                                        continue;

                                    if (!action2.Interruptable)
                                        continue;

                                    if (action2.actionState == ActionState.Succeed)
                                        continue;

                                    action2.actionState = ActionState.Suspended;
                                }
                            }
                            break;
                    }
                }
            }

            foreach (KeyValuePair<AxisLayoutFlags, IActions> axisLayout in currentLayout.AxisLayout)
            {
                AxisLayoutFlags flags = axisLayout.Key;

                // read origin values
                AxisLayout InLayout = AxisLayout.Layouts[flags];
                AxisFlags InAxisX = InLayout.GetAxisFlags('X');
                AxisFlags InAxisY = InLayout.GetAxisFlags('Y');

                InLayout.vector.X = controllerState.AxisState[InAxisX];
                InLayout.vector.Y = controllerState.AxisState[InAxisY];

                // pull action
                IActions action = axisLayout.Value;

                if (action is null)
                    continue;

                switch (action.actionType)
                {
                    case ActionType.Joystick:
                        {
                            AxisActions aAction = action as AxisActions;
                            aAction.Execute(InLayout);

                            // read output axis
                            AxisLayout OutLayout = AxisLayout.Layouts[aAction.Axis];
                            AxisFlags OutAxisX = OutLayout.GetAxisFlags('X');
                            AxisFlags OutAxisY = OutLayout.GetAxisFlags('Y');

                            outputState.AxisState[OutAxisX] = (short)Math.Clamp(outputState.AxisState[OutAxisX] + aAction.GetValue().X, short.MinValue, short.MaxValue);
                            outputState.AxisState[OutAxisY] = (short)Math.Clamp(outputState.AxisState[OutAxisY] + aAction.GetValue().Y, short.MinValue, short.MaxValue);
                        }
                        break;

                    case ActionType.Trigger:
                        {
                            TriggerActions tAction = action as TriggerActions;
                            tAction.Execute(InAxisY, (short)InLayout.vector.Y);

                            // read output axis
                            AxisLayout OutLayout = AxisLayout.Layouts[tAction.Axis];
                            AxisFlags OutAxisY = OutLayout.GetAxisFlags('Y');

                            outputState.AxisState[OutAxisY] = (short)Math.Clamp(outputState.AxisState[OutAxisY] + tAction.GetValue(), short.MinValue, short.MaxValue);
                        }
                        break;

                    case ActionType.Mouse:
                        {
                            MouseActions mAction = action as MouseActions;

                            // This buttonState check won't work here if UpdateInputs is event based, might need a rework in the future
                            bool touched = false;
                            if (ControllerState.AxisTouchButtons.TryGetValue(InLayout.flags, out ButtonFlags touchButton))
                                touched = controllerState.ButtonState[touchButton];

                            mAction.Execute(InLayout, touched);
                        }
                        break;
                }
            }

            foreach (var axisLayout in currentLayout.GyroLayout)
            {
                AxisLayoutFlags flags = axisLayout.Key;

                // read origin values
                AxisLayout InLayout = AxisLayout.Layouts[flags];
                AxisFlags InAxisX = InLayout.GetAxisFlags('X');
                AxisFlags InAxisY = InLayout.GetAxisFlags('Y');

                InLayout.vector.X = controllerState.AxisState[InAxisX];
                InLayout.vector.Y = controllerState.AxisState[InAxisY];

                // pull action
                IActions action = axisLayout.Value;

                if (action is null)
                    continue;

                switch (action.actionType)
                {
                    case ActionType.Joystick:
                        {
                            AxisActions aAction = action as AxisActions;
                            aAction.Execute(InLayout);

                            // Read output axis
                            AxisLayout OutLayout = AxisLayout.Layouts[aAction.Axis];
                            AxisFlags OutAxisX = OutLayout.GetAxisFlags('X');
                            AxisFlags OutAxisY = OutLayout.GetAxisFlags('Y');

                            Vector2 joystick = new Vector2(outputState.AxisState[OutAxisX], outputState.AxisState[OutAxisY]);

                            // Reduce motion weight based on joystick position
                            // Get the distance of the joystick from the center
                            float joystickLength = Math.Clamp(joystick.Length() / short.MaxValue, 0, 1);
                            float weightFactor = aAction.gyroWeight - joystickLength;
                            Vector2 result = joystick + aAction.GetValue() * weightFactor;

                            // Apply clamping to the result to stay in range of joystick
                            outputState.AxisState[OutAxisX] = (short)Math.Clamp(result.X, short.MinValue, short.MaxValue);
                            outputState.AxisState[OutAxisY] = (short)Math.Clamp(result.Y, short.MinValue, short.MaxValue);
                        }
                        break;

                    case ActionType.Mouse:
                        {
                            MouseActions mAction = action as MouseActions;

                            // This buttonState check won't work here if UpdateInputs is event based, might need a rework in the future
                            bool touched = false;
                            if (ControllerState.AxisTouchButtons.TryGetValue(InLayout.flags, out ButtonFlags touchButton))
                                touched = controllerState.ButtonState[touchButton];

                            mAction.Execute(InLayout, touched);
                        }
                        break;
                }
            }

            return outputState;
        }
    }

    #region events

    public static event InitializedEventHandler Initialized;
    public delegate void InitializedEventHandler();

    public static event UpdatedEventHandler Updated;
    public delegate void UpdatedEventHandler(LayoutTemplate layoutTemplate);

    #endregion
}