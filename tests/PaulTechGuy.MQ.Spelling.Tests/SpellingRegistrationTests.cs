// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.DependencyInjection;
using PaulTechGuy.MQ.Abstractions.Spelling;
using Shouldly;
using Xunit;

namespace PaulTechGuy.MQ.Spelling.Tests;

/// <summary>
/// What this layer registers, and — more importantly — what it must not.
///
/// This exists because of a real bug. The layer used to register a do-nothing
/// <c>IUserDictionary</c> as a fallback, on the reasoning that the library should work on its own.
/// TryAdd keeps the first registration and this layer is registered before the one that supplies
/// the real word list, so the fallback silently won: the analyzer spent its life filtering against
/// an empty list, and Add to Dictionary appeared to do nothing at all while writing the file
/// correctly the whole time.
///
/// A null object that is indistinguishable from a working implementation is worse than no
/// registration. Missing means a loud failure at startup naming the interface; present-but-empty
/// means a feature that looks implemented and is not.
/// </summary>
public sealed class SpellingRegistrationTests
{
    [Fact]
    public void The_analyzer_is_registered()
    {
        Registered<ISpellingAnalyzer>().ShouldBeTrue();
    }

    [Fact]
    public void No_word_list_is_registered_here()
    {
        // The host supplies it. If this ever fails, read the class comment above before
        // "fixing" it by adding one back.
        Registered<IUserDictionary>().ShouldBeFalse();
    }

    [Fact]
    public void No_engine_is_registered_here()
    {
        // The only implementation is Windows COM, which belongs to the app layer.
        Registered<ISpellingEngine>().ShouldBeFalse();
    }

    private static bool Registered<T>() =>
        new ServiceCollection()
            .AddMarqoraSpelling()
            .Any(descriptor => descriptor.ServiceType == typeof(T));
}
