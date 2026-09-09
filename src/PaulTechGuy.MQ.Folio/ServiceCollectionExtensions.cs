// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;

namespace PaulTechGuy.MQ.Folio;

/// <summary>Registers the Folio layer, so the composition root stays a list of intents.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMarqoraFolio(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The planner is static: paths in, a plan out, nothing to hold.
        services.AddSingleton<FolioWriter>();

        return services;
    }
}
