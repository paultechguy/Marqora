// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaulTechGuy.MQ.Abstractions.Editing;

namespace PaulTechGuy.MQ.Editing;

/// <summary>Registration for the markdown authoring commands.</summary>
public static class EditingServiceCollectionExtensions
{
    public static IServiceCollection AddMarqoraEditing(this IServiceCollection services)
    {
        // Stateless, so one instance serves the whole app.
        services.TryAddSingleton<IMarkdownEditor, MarkdownEditor>();

        return services;
    }
}
