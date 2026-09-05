// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Services;
using PaulTechGuy.MQ.Abstractions.Spelling;

namespace PaulTechGuy.MQ.Services;

/// <summary>Registration for the application service layer.</summary>
public static class ServiceCollectionExtensions
{
    /// <param name="appVersion">
    /// The running build's version, which the welcome document compares against the last one
    /// it introduced. It is passed in rather than read here because the version belongs to
    /// the executable, and this layer is a library that a test host also loads.
    /// </param>
    /// <param name="welcomeRequested">
    /// True when this launch asked for the welcome document outright, which is Shift held as
    /// the app starts. Read at the composition root for the same reason as the version: it is
    /// a fact about how the process was started.
    /// </param>
    public static IServiceCollection AddMarqoraServices(
        this IServiceCollection services,
        string appVersion,
        bool welcomeRequested = false)
    {
        services.TryAddSingleton<ISettingsService, SettingsService>();
        services.TryAddSingleton<IRecentFilesService, RecentFilesService>();

        services.TryAddSingleton<IWelcomeDocumentService>(provider => new WelcomeDocumentService(
            provider.GetRequiredService<IAppPaths>(),
            provider.GetRequiredService<ISettingsService>(),
            appVersion,
            welcomeRequested,
            provider.GetRequiredService<ILogger<WelcomeDocumentService>>()));

        // One watcher per open document, so the factory is the singleton, not the watcher.
        // Registered under both: the analyzer talks to IUserDictionary, and startup needs the
        // concrete one to read the file and start watching it.
        services.TryAddSingleton<UserDictionaryService>();
        services.TryAddSingleton<IUserDictionary>(p => p.GetRequiredService<UserDictionaryService>());

        services.TryAddSingleton<IFileWatcherFactory, FileWatcherFactory>();
        services.TryAddSingleton<IWorkspaceService, DocumentWorkspace>();

        return services;
    }
}
