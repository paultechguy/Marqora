// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaulTechGuy.MQ.Abstractions.Analysis;

namespace PaulTechGuy.MQ.Analysis;

/// <summary>Registration for the document checks.</summary>
public static class AnalysisServiceCollectionExtensions
{
    public static IServiceCollection AddMarqoraAnalysis(this IServiceCollection services)
    {
        // Stateless, so one instance serves the whole app.
        services.TryAddSingleton<IMarkdownAnalyzer, MarkdownAnalyzer>();

        return services;
    }
}
