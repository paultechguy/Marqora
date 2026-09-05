// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaulTechGuy.MQ.Abstractions.Spelling;

namespace PaulTechGuy.MQ.Spelling;

public static class SpellingServiceCollectionExtensions
{
    /// <summary>
    /// The analyzer. Nothing else.
    ///
    /// Neither ISpellingEngine nor IUserDictionary is registered here: the only implementation is Windows
    /// COM and belongs to the app layer, and the word list is a file the services layer owns.
    /// Both are the composition root's to supply, the same way the analysis layer assumes a
    /// renderer has run.
    /// </summary>
    public static IServiceCollection AddMarqoraSpelling(this IServiceCollection services)
    {
        // Singleton because the analyzer carries the line cache, and a cache built per document
        // would throw away every hit that makes the feature affordable.
        services.TryAddSingleton<ISpellingAnalyzer, SpellingAnalyzer>();

        // IUserDictionary is deliberately NOT registered here, and there is no null-object
        // fallback for it. There used to be one, and it silently won: TryAdd keeps the first
        // registration, this method runs before AddMarqoraServices, and the analyzer spent its
        // life filtering against an empty list that never learned a word. Add to Dictionary
        // appeared to do nothing. A missing registration now fails loudly at startup instead,
        // which is the far cheaper failure.

        return services;
    }
}
