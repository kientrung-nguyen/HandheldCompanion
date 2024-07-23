﻿using HandheldCompanion.Managers;
using HandheldCompanion.Utils;
using HandheldCompanion.Views;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Sentry;
using Sentry.Profiling;
using static HandheldCompanion.WinAPI;

namespace HandheldCompanion;

/// <summary>
///     Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static bool IsMultiThreaded { get; } = false;

    /// <summary>
    ///     Initializes the singleton application object.  This is the first line of authored code
    ///     executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeSentry();
        InitializeComponent();
    }

    /// <summary>
    ///     Invoked when the application is launched normally by the end user.  Other entry points
    ///     will be used such as when the application is launched to open a specific file.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnStartup(StartupEventArgs args)
    {
        // get current assembly
        var CurrentAssembly = Assembly.GetExecutingAssembly();
        var fileVersionInfo = FileVersionInfo.GetVersionInfo(CurrentAssembly.Location);

        // set environment variables
        Environment.SetEnvironmentVariable("APP_BASE_DIRECTORY", AppContext.BaseDirectory);

        // initialize log
        LogManager.Initialize("HandheldCompanion");
        LogManager.LogInformation("{0} ({1})", CurrentAssembly.GetName(), fileVersionInfo.FileVersion);

        using (var process = Process.GetCurrentProcess())
        {
            // force high priority
            SetPriorityClass(process.Handle, (int)PriorityClass.HIGH_PRIORITY_CLASS);

            Process[] processes = Process.GetProcessesByName(process.ProcessName);
            if (processes.Length > 1)
            {
                using (Process prevProcess = processes[0])
                {
                    nint handle = prevProcess.MainWindowHandle;
                    if (ProcessUtils.IsIconic(handle))
                        ProcessUtils.ShowWindow(handle, (int)ProcessUtils.ShowWindowCommands.Restored);

                    // force close this process if we were able to bring previous process to foreground
                    // kill previous process otherwise (means it's stalled)
                    if (ProcessUtils.SetForegroundWindow(handle))
                        process.Kill();
                    else
                        prevProcess.Kill();

                    return;
                }
            }
        }

        // define culture settings
        var CurrentCulture = SettingsManager.GetString("CurrentCulture");
        var culture = CultureInfo.CurrentCulture;

        switch (CurrentCulture)
        {
            default:
                culture = new CultureInfo("en-US");
                break;
            case "fr-FR":
            case "en-US":
            case "zh-Hans":
            case "zh-Hant":
            case "de-DE":
            case "it-IT":
            case "pt-BR":
            case "es-ES":
            case "ja-JP":
            case "ru-RU":
                culture = new CultureInfo(CurrentCulture);
                break;
            case "zh-CN": // fallback change locale name from zh-CN to zh-Hans
                SettingsManager.SetProperty("CurrentCulture", "zh-Hans", true);
                CurrentCulture = "zh-Hans";
                culture = new CultureInfo(CurrentCulture);
                break;
        }

        Localization.TranslationSource.Instance.CurrentCulture = culture;

        // handle exceptions nicely
        var currentDomain = default(AppDomain);
        currentDomain = AppDomain.CurrentDomain;
        // Handler for unhandled exceptions.
        currentDomain.UnhandledException += CurrentDomain_UnhandledException;
        // Handler for exceptions in threads behind forms.
        System.Windows.Forms.Application.ThreadException += Application_ThreadException;
        // Handler for exceptions in dispatcher.
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        MainWindow = new MainWindow(fileVersionInfo, CurrentAssembly);
        MainWindow.Show();
    }

    private void Application_ThreadException(object sender, ThreadExceptionEventArgs e)
    {
        Exception ex = (Exception)e.Exception;

        // send to sentry
        bool IsSentryEnabled = SettingsManager.GetBoolean("TelemetryEnabled");
        if (SentrySdk.IsEnabled && IsSentryEnabled)
            SentrySdk.CaptureException(ex);

        if (ex.InnerException != null)
            LogManager.LogCritical(ex.InnerException.Message + "\t" + ex.InnerException.StackTrace);

        LogManager.LogCritical(ex.Message + "\t" + ex.StackTrace);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Exception ex = (Exception)e.ExceptionObject;

        // send to sentry
        bool IsSentryEnabled = SettingsManager.GetBoolean("TelemetryEnabled");
        if (SentrySdk.IsEnabled && IsSentryEnabled)
            SentrySdk.CaptureException(ex);

        if (ex.InnerException != null)
            LogManager.LogCritical(ex.InnerException.Message + "\t" + ex.InnerException.StackTrace);

        LogManager.LogCritical(ex.Message + "\t" + ex.StackTrace);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Exception ex = (Exception)e.Exception;

        // send to sentry
        bool IsSentryEnabled = SettingsManager.GetBoolean("TelemetryEnabled");
        if (SentrySdk.IsEnabled && IsSentryEnabled)
            SentrySdk.CaptureException(ex);

        if (ex.InnerException != null)
            LogManager.LogCritical(ex.InnerException.Message + "\t" + ex.InnerException.StackTrace);

        LogManager.LogCritical(ex.Message + "\t" + ex.StackTrace);

        // If you want to avoid the application from crashing:
        e.Handled = true;
    }

    private void InitializeSentry()
    {
        string url = SentryConfig.DSN_URL;

        if (!string.IsNullOrEmpty(url))
        {            
            SentrySdk.Init(options =>
            {
                // Tells which project in Sentry to send events to:
                options.Dsn = url;

                #if DEBUG
                // When configuring for the first time, to see what the SDK is doing:
                options.Debug = true;
                #else
                options.Debug = false;
                #endif
                
                // Enable Global Mode since this is a client app
                options.IsGlobalModeEnabled = true;
            });
        }
    }
}