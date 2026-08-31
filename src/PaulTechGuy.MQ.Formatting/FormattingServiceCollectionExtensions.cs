// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaulTechGuy.MQ.Abstractions.Formatting;

namespace PaulTechGuy.MQ.Formatting;

/// <summary>Registration for the markdown formatting layer.</summary>
public static class FormattingServiceCollectionExtensions
{
    public static IServiceCollection AddMarqoraFormatting(this IServiceCollection services)
    {
        // Stateless, so one instance serves the whole app.
        services.TryAddSingleton<IMarkdownFormatter, MarkdownFormatter>();

        return services;
    }
}
