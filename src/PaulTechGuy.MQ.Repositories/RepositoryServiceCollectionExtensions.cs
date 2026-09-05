// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions;
using PaulTechGuy.MQ.Abstractions.Repositories;
using PaulTechGuy.MQ.Abstractions.Services;

namespace PaulTechGuy.MQ.Repositories;

/// <summary>
/// Registration for the persistence layer. Each layer owns its own registration
/// extension so the composition root stays a list of AddX calls.
/// </summary>
public static class RepositoryServiceCollectionExtensions
{
    /// <param name="appVersion">
    /// Stamped into an exported preferences file so the machine that reads it can say where it
    /// came from. Passed in rather than read here: the version belongs to the executable, and
    /// this assembly is not it.
    /// </param>
    public static IServiceCollection AddMarqoraRepositories(
        this IServiceCollection services,
        string appVersion)
    {
        services.TryAddSingleton<IAppPaths, AppPaths>();
        services.TryAddSingleton<ISettingsRepository, JsonSettingsRepository>();
        services.TryAddSingleton<IRecentFilesRepository, JsonRecentFilesRepository>();
        services.TryAddSingleton<IUserDictionaryRepository, TextUserDictionaryRepository>();
        services.TryAddSingleton<ISnippetCatalog, SnippetCatalog>();

        services.TryAddSingleton<IPreferencesTransfer>(provider => new PreferencesTransferService(
            appVersion,
            provider.GetRequiredService<ILogger<PreferencesTransferService>>()));

        return services;
    }
}
