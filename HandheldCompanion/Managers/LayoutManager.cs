using HandheldCompanion.Actions;
using HandheldCompanion.Controllers;
using HandheldCompanion.Helpers;
using HandheldCompanion.Inputs;
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

namespace HandheldCompanion.Managers;

public class LayoutManager : IManager
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

    private object updateLock = new();

    private Layout currentLayout = new();
    private Layout profileLayout = new();
    private Layout defaultLayout = null;
    private Layout desktopLayout = null;

    private ControllerState outputState = new();

    private const string desktopLayoutFile = "desktop";

    public string LayoutsPath;
    public string TemplatesPath;

    public FileSystemWatcher layoutWatcher { get; set; }

    public LayoutManager()
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

    public override void Start()
    {
        if (Status == ManagerStatus.Initializing || Status == ManagerStatus.Initialized)
            return;

        base.PrepareStart();

        // process community templates
        string[] fileEntries = Directory.GetFiles(TemplatesPath, "*.json", SearchOption.AllDirectories);
        foreach (string fileName in fileEntries)
            ProcessLayoutTemplate(fileName);

        // process default templates
        foreach (LayoutTemplate layoutTemplate in Templates)
            Updated?.Invoke(layoutTemplate);

        string desktopFile = Path.Combine(LayoutsPath, $"{desktopLayoutFile}.json");
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
        ProfileManager.Initialized += ProfileManager_Initialized;
        ManagerFactory.settingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        // raise events
        switch (ManagerFactory.settingsManager.Status)
        {
            default:
            case ManagerStatus.Initializing:
                ManagerFactory.settingsManager.Initialized += SettingsManager_Initialized;
                break;
            case ManagerStatus.Initialized:
                QuerySettings();
                break;
        }

        switch (ManagerFactory.multimediaManager.Status)
        {
            default:
            case ManagerStatus.Initializing:
                ManagerFactory.multimediaManager.Initialized += MultimediaManager_Initialized;
                break;
            case ManagerStatus.Initialized:
                QueryMedia();
                break;
        }

        if (ProfileManager.IsInitialized)
        {
            ProfileManager_Applied(ProfileManager.GetCurrent(), UpdateSource.Background);
        }

        base.Start();
    }

    private void MultimediaManager_Initialized()
    {
        QueryMedia();
    }

    private void QueryMedia()
    {
        // do something
    }

    private void QuerySettings()
    {
        ManagerFactory.settingsManager.SetProperty("DesktopLayoutEnabled", ManagerFactory.settingsManager.GetBoolean("DesktopProfileOnStart"), false, true);
    }

    private void SettingsManager_Initialized()
    {
        QuerySettings();
    }

    public override void Stop()
    {
        if (Status == ManagerStatus.Halting || Status == ManagerStatus.Halted)
            return;

        base.PrepareStop();

        // manage desktop layout events
        desktopLayout.Updated -= DesktopLayout_Updated;

        // manage layout watcher events
        layoutWatcher.Created -= LayoutWatcher_Template;
        layoutWatcher.Changed -= LayoutWatcher_Template;

        // manage events
        ProfileManager.Applied -= ProfileManager_Applied;
        ProfileManager.Initialized -= ProfileManager_Initialized;
        ManagerFactory.settingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;
        ManagerFactory.settingsManager.Initialized -= SettingsManager_Initialized;

        base.Stop();
    }

    // this event is called from non main thread and it creates LayoutTemplate which is a WPF element
    private void LayoutWatcher_Template(object sender, FileSystemEventArgs e)
    {
        // UI thread
        UIHelper.TryInvoke(() =>
        {
            ProcessLayoutTemplate(e.FullPath);
        });
    }

    private Layout? ProcessLayout(string fileName)
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

    private void ProcessLayoutTemplate(string fileName)
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

    private void DesktopLayout_Updated(Layout layout)
    {
        SerializeLayout(layout, desktopLayoutFile);
    }

    private void ProfileManager_Applied(Profile profile, UpdateSource source)
    {
        SetProfileLayout(profile);
    }

    private void ProfileManager_Initialized()
    {
        // ref
        defaultLayout = ProfileManager.GetDefault().Layout;
        defaultLayout.Updated += DefaultLayout_Updated;
    }

    private void DefaultLayout_Updated(Layout layout)
    {
        UpdateInherit();
    }

    private void SetProfileLayout(Profile profile = null)
    {
        // use profile layout (will be cloned during SetActiveLayout)
        // ref
        profileLayout = profile.Layout;

        // only update current layout if we're not into desktop layout mode
        if (!ManagerFactory.settingsManager.GetBoolean("DesktopLayoutEnabled", true))
            SetActiveLayout(profileLayout);
    }

    public Layout GetCurrent()
    {
        return currentLayout;
    }

    public Layout GetDesktop()
    {
        return desktopLayout;
    }

    public void SerializeLayout(Layout layout, string fileName)
    {
        var jsonString = JsonConvert.SerializeObject(layout, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All
        });

        fileName = Path.Combine(LayoutsPath, $"{fileName}.json");
        if (FileUtils.IsFileWritable(fileName))
            File.WriteAllText(fileName, jsonString);
    }

    public void SerializeLayoutTemplate(LayoutTemplate layoutTemplate)
    {
        string fileName = Path.Combine(TemplatesPath, $"{layoutTemplate.Name}_{layoutTemplate.Author}.json");
        if (File.Exists(fileName))
        {
            // get previous template with same name and author
            LayoutTemplate template = Templates.FirstOrDefault(t => t.Name == layoutTemplate.Name && t.Author == layoutTemplate.Author);
            if (template is not null)
                layoutTemplate.Guid = template.Guid;
        }

        var jsonString = JsonConvert.SerializeObject(layoutTemplate, Formatting.Indented, new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.All
        });

        if (FileUtils.IsFileWritable(fileName))
            File.WriteAllText(fileName, jsonString);
    }

    private void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
    {
        switch (name)
        {
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

    private void SetActiveLayout(Layout layout)
    {
        lock (updateLock)
        {
            // clone
            currentLayout = layout.Clone() as Layout;

            // (re)apply inheritance
            UpdateInherit();
        }
    }

    private void UpdateInherit()
    {
        lock (updateLock)
        {
            // Check for inherit(s) and replace actions with default layout actions where necessary
            IController controller = ControllerManager.GetTargetOrDefault();
            if (controller is not null)
            {
                foreach (ButtonFlags buttonFlags in controller.GetTargetButtons())
                {
                    if (currentLayout.ButtonLayout.TryGetValue(buttonFlags, out var actions) && actions.Any(action => action is InheritActions))
                    {
                        // Replace with default layout actions
                        if (defaultLayout.ButtonLayout.TryGetValue(buttonFlags, out var defaultActions))
                            currentLayout.ButtonLayout[buttonFlags].AddRange(defaultActions);
                    }
                }

                // Check for inherit(s) and replace actions with default layout actions where necessary
                foreach (AxisLayoutFlags axisLayout in controller.GetTargetAxis().Union(controller.GetTargetTriggers()))
                {
                    if (currentLayout.AxisLayout.TryGetValue(axisLayout, out var actions) && actions is InheritActions)
                    {
                        // Replace with default layout actions
                        if (defaultLayout.AxisLayout.TryGetValue(axisLayout, out var defaultActions))
                            currentLayout.AxisLayout[axisLayout] = defaultActions;
                    }
                }
            }
        }
    }

    public ControllerState MapController(ControllerState controllerState)
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

    public event UpdatedEventHandler Updated;
    public delegate void UpdatedEventHandler(LayoutTemplate layoutTemplate);

    #endregion
}