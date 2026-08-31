// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using PaulTechGuy.MQ.App.Services;
using PaulTechGuy.MQ.App.Views;
using Serilog;

namespace PaulTechGuy.MQ.App;

/// <summary>
/// Application object. Owns the DI host for the process lifetime and opens the main window.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;
    private readonly ActivationRouter _router;
    private readonly IReadOnlyList<string> _startupFiles;

    private MainWindow? _window;

    public App(IHost host, ActivationRouter router, IReadOnlyList<string> startupFiles)
    {
        _host = host;
        _router = router;
        _startupFiles = startupFiles;

        Services = host.Services;

        InitializeComponent();

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Resolved services. XAML-constructed types cannot take constructor injection, so a
    /// small number of them reach the container through here; everything else is injected.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow window = Services.GetRequiredService<MainWindow>();

        _window = window;
        _window.Closed += OnWindowClosed;

        _window.Activate();

        if (_startupFiles.Count > 0)
        {
            Log.Information("Opening {Count} file(s) from the command line.", _startupFiles.Count);
            window.OpenAtStartup(_startupFiles);
        }

        // Files from later launches arrive here. Anything that queued while the window was
        // being built is delivered as soon as this hands over a destination.
        _router.Attach(window.OpenFromActivation);
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // Give the debounced settings writer a chance to land before the process ends.
        try
        {
            _host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            _host.Dispose();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Shutdown did not complete cleanly.");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled UI exception: {Message}", e.Message);

        // Keeping the window alive is nearly always better than losing the open document.
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e) =>
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception on a background thread.");

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }
}
