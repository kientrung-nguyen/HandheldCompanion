﻿using HandheldCompanion.Shared;
using Microsoft.Win32.TaskScheduler;
using System;
using System.Security.Principal;

namespace HandheldCompanion.Managers;

public static class TaskManager
{
    private const string TaskName = "HandheldCompanion";
    private static string TaskExecutable;

    // TaskManager vars
    private static Task task;
    private static TaskDefinition taskDefinition;
    private static TaskService taskService;

    private static bool IsInitialized;

    public static event InitializedEventHandler Initialized;
    public delegate void InitializedEventHandler();

    static TaskManager()
    {
    }

    public static async System.Threading.Tasks.Task Start(string Executable)
    {
        if (IsInitialized)
            return;

        TaskExecutable = Executable;
        taskService = new TaskService();

        try
        {
            // get current task, if any, delete it
            task = taskService.FindTask(TaskName);
            if (task is not null)
                taskService.RootFolder.DeleteTask(TaskName);
        }
        catch { }

        try
        {
            // create a new task
            taskDefinition = TaskService.Instance.NewTask();
            taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
            taskDefinition.Principal.UserId = WindowsIdentity.GetCurrent().Name;
            taskDefinition.Principal.LogonType = TaskLogonType.InteractiveToken;
            taskDefinition.Settings.DisallowStartIfOnBatteries = false;
            taskDefinition.Settings.StopIfGoingOnBatteries = false;
            taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.Zero;
            taskDefinition.Settings.Enabled = false;
            taskDefinition.Triggers.Add(new LogonTrigger() { UserId = WindowsIdentity.GetCurrent().Name });
            taskDefinition.Actions.Add(new ExecAction(TaskExecutable));

            task = TaskService.Instance.RootFolder.RegisterTaskDefinition(TaskName, taskDefinition);
            task.Enabled = SettingsManager.GetBoolean("RunAtStartup");
        }
        catch { }

        // manage events
        SettingsManager.SettingValueChanged += SettingsManager_SettingValueChanged;

        // raise events
        if (SettingsManager.IsInitialized)
        {
            SettingsManager_SettingValueChanged("RunAtStartup", SettingsManager.GetString("RunAtStartup"), false);
        }

        IsInitialized = true;
        Initialized?.Invoke();

        LogManager.LogInformation("{0} has started", "TaskManager");
    }

    public static void Stop()
    {
        if (!IsInitialized)
            return;

        // manage events
        SettingsManager.SettingValueChanged -= SettingsManager_SettingValueChanged;

        IsInitialized = false;

        LogManager.LogInformation("{0} has stopped", "TaskManager");
    }

    private static void SettingsManager_SettingValueChanged(string name, object value, bool temporary)
    {
        switch (name)
        {
            case "RunAtStartup":
                UpdateTask(Convert.ToBoolean(value));
                break;
        }
    }

    private static void UpdateTask(bool value)
    {
        if (task is null)
            return;

        try
        {
            task.Enabled = value;
        }
        catch
        {
        }
    }
}