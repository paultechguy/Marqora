// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaulTechGuy.MQ.Abstractions.Rendering;

namespace PaulTechGuy.MQ.Rendering;

/// <summary>Registration for the markdown rendering layer.</summary>
public static class RenderingServiceCollectionExtensions
{
    public static IServiceCollection AddMarqoraRendering(this IServiceCollection services)
    {
        // The Markdig pipeline is immutable once built, so one instance serves the whole app.
        services.TryAddSingleton<IMarkdownRenderer, MarkdigMarkdownRenderer>();
        services.TryAddSingleton<IWebAssetProvider, WebAssetProvider>();

        return services;
    }
}
