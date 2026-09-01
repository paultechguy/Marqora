// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Ui;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.App.ViewModels;
using PaulTechGuy.MQ.App.Views;
using PaulTechGuy.MQ.Analysis;
using PaulTechGuy.MQ.Domain;
using PaulTechGuy.MQ.Editing;
using PaulTechGuy.MQ.Formatting;
using PaulTechGuy.MQ.Rendering;
using PaulTechGuy.MQ.Repositories;
using PaulTechGuy.MQ.Services;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace PaulTechGuy.MQ.App;

/// <summary>
/// Composition root.
///
/// The XAML compiler would normally generate this entry point; DISABLE_XAML_GENERATED_MAIN
/// hands it over so logging and the DI container are both live before any UI is created,
/// which means a failure during startup lands in the log rather than vanishing.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        // WebView2 otherwise writes its cache beside the executable, which fails when the
        // app is installed somewhere the user cannot write.
        Environment.SetEnvironmentVariable(
            "WEBVIEW2_USER_DATA_FOLDER",
            Path.Combine(paths.DataDirectory, "WebView2"));

        Log.Logger = CreateBootstrapLogger(paths);

        try
        {
            Log.Information("Marqora starting. Data directory: {DataDirectory}", paths.DataDirectory);

            // The WinRT projections have to be live before any Windows App SDK type is
            // touched, and single-instance registration below is the first one.
            WinRT.ComWrappersSupport.InitializeComWrappers();

            var router = new ActivationRouter();

            // Asked before anything else can steal the keyboard: this is the state of the
            // Shift key at the moment the user started the app, and it means "show me the
            // welcome document" whether or not this version has already shown it.
            bool welcomeRequested = StartupModifiers.IsShiftHeld();

            // Marqora is single-instance: a second launch hands its files to the window that
            // is already open and stops here. --new-instance opts out for debugging two
            // windows side by side.
            if (!SingleInstance.WantsNewInstance(args) && SingleInstance.TryHandOff(router))
            {
                if (welcomeRequested)
                {
                    // The redirect carries files and nothing else, so the request cannot be
                    // passed on. Said out loud rather than swallowed: otherwise holding Shift
                    // while Marqora is already running looks like a broken gesture.
                    Log.Information(
                        "Shift was held, but this launch was handed to the running instance; "
                        + "close Marqora first to have the welcome document opened.");
                }

                Log.Information("Handed this launch to the running instance.");
                return;
            }

            if (welcomeRequested)
            {
                Log.Information("Shift was held at launch: the welcome document will be opened.");
            }

            IHost host = BuildHost(paths, args, welcomeRequested);

            Microsoft.UI.Xaml.Application.Start(_unusedInitParams =>
            {
                // WinUI runs on a DispatcherQueue rather than a classic message pump, so the
                // synchronization context has to be installed by hand for await to resume on
                // the UI thread.
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);

                _ = new App(host, router, SingleInstance.FilesFrom(args));
            });
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Marqora terminated unexpectedly during startup.");
            throw;
        }
        finally
        {
            Log.Information("Marqora exiting.");
            Log.CloseAndFlush();
        }
    }

    private static IHost BuildHost(AppPaths paths, string[] args, bool welcomeRequested)
    {
        // appsettings.json ships next to the executable, which is not necessarily the
        // working directory the process was launched from.
        HostApplicationBuilder builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = "Marqora",
        });

        builder.Services.AddSerilog((_, configuration) => ConfigureLogging(configuration, builder, paths));

        // Each layer contributes its own registrations, so this stays a list of intents.
        // Registered under the interface so the whole app shares this one instance, which
        // logging already used before the container existed.
        builder.Services.AddSingleton<IAppPaths>(paths);
        builder.Services.AddMarqoraRepositories(AppVersion.Current);
        builder.Services.AddMarqoraRendering();
        builder.Services.AddMarqoraFormatting();
        builder.Services.AddMarqoraEditing();
        builder.Services.AddMarqoraAnalysis();
        builder.Services.AddMarqoraServices(AppVersion.Current, welcomeRequested);

        // UI-layer implementations of the shared abstractions.
        builder.Services.AddSingleton<IFileDialogService, FileDialogService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<IUiDispatcher, UiDispatcher>();
        builder.Services.AddSingleton<RenderedHtmlPackager>();
        builder.Services.AddSingleton<IHtmlExporter, HtmlExporter>();
        builder.Services.AddSingleton<IExportDialogService, ExportDialogService>();
        builder.Services.AddSingleton<IPrintDialogService, PrintDialogService>();
        builder.Services.AddSingleton<IFormatDialogService, FormatDialogService>();
        builder.Services.AddSingleton<IPreferencesDialogService, PreferencesDialogService>();
        builder.Services.AddSingleton<ICheatsheetService, CheatsheetService>();
        builder.Services.AddSingleton<IDiagramWindowService, DiagramWindowService>();
        builder.Services.AddSingleton<IFindAllWindowService, FindAllWindowService>();
        builder.Services.AddSingleton<WindowContext>();

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        return builder.Build();
    }

    private static void ConfigureLogging(
        LoggerConfiguration configuration,
        HostApplicationBuilder builder,
        AppPaths paths)
    {
        // Read straight off disk rather than through ISettingsService: logging is configured
        // while the container is still being built, and everything in the container wants a
        // logger. A retention change therefore lands at the next launch, which the
        // preferences page says. Zero means keep everything, which is what Serilog reads a
        // null limit as.
        int retentionDays = SettingsFile.ReadOrDefault(paths.SettingsFilePath).LogRetentionDays;

        // One file per day, so a count of files is a count of days.
        int? retainedFiles = retentionDays > 0
            ? Math.Min(retentionDays, AppSettings.MaximumLogRetentionDays)
            : null;

        configuration
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext()
            // Logs are diagnostic text, not user-facing, so they are written in a fixed
            // culture. Otherwise timestamps and numbers would change shape by machine.
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
            // Async keeps file I/O off the UI thread; the sink is flushed by CloseAndFlush.
            .WriteTo.Async(sink => sink.File(
                Path.Combine(paths.LogDirectory, "marqora-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retainedFiles,
                fileSizeLimitBytes: 16 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}"));
    }

    /// <summary>
    /// A minimal logger that covers the window between process start and the host being
    /// built, so configuration errors are still recorded.
    /// </summary>
    private static Logger CreateBootstrapLogger(AppPaths paths) =>
        new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "marqora-.log"),
                rollingInterval: RollingInterval.Day,
                formatProvider: CultureInfo.InvariantCulture,
                restrictedToMinimumLevel: LogEventLevel.Information)
            .CreateLogger();

}
