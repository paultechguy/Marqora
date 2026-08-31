// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    public static IServiceCollection AddMarqoraRepositories(this IServiceCollection services)
    {
        services.TryAddSingleton<IAppPaths, AppPaths>();
        services.TryAddSingleton<ISettingsRepository, JsonSettingsRepository>();
        services.TryAddSingleton<IRecentFilesRepository, JsonRecentFilesRepository>();
        services.TryAddSingleton<ISnippetCatalog, SnippetCatalog>();

        return services;
    }
}
