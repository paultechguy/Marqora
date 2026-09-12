// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaulTechGuy.MQ.Abstractions.Ui;

namespace PaulTechGuy.MQ.Docx;

/// <summary>Registration for the Word export layer.</summary>
public static class DocxServiceCollectionExtensions
{
    public static IServiceCollection AddMarqoraDocx(this IServiceCollection services)
    {
        // Holds a Markdig pipeline, which is immutable after Build, so one instance serves
        // the whole app - the same reasoning as AddMarqoraRendering.
        services.TryAddSingleton<IDocxExporter, DocxExporter>();

        return services;
    }
}
