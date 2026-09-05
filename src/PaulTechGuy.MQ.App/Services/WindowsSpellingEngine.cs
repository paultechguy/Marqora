// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using PaulTechGuy.MQ.Abstractions.Spelling;
using PaulTechGuy.MQ.Domain;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace PaulTechGuy.MQ.App.Services;

/// <summary>
/// The Windows spell checking service, which has shipped with the OS since Windows 8 and is the
/// same one Word and Edge use. No dictionary is bundled and no package is referenced: the words
/// come from the language packs the machine already has.
///
/// It lives in the app project rather than in a library because it is Windows-only COM, and
/// CA1416 is not suppressed in .editorconfig — a net10.0 library doing this would warn on every
/// build. Win32Dialogs is the neighbour that does the same thing for the file dialogs, and the
/// interop below follows its shape: an RCW class per CLSID, interfaces redeclared in vtable
/// order, and PreserveSig only where the HRESULT carries meaning rather than failure.
///
/// <b>Threading.</b> One checker is created and every call is serialized behind a lock. The
/// object is created on whichever thread asks first, and that should be a thread-pool one:
/// analysis already runs through Task.Run, and <see cref="WarmUpAsync"/> exists so that startup
/// wins the race rather than a preferences dialog opened on the UI thread.
/// </summary>
internal sealed class WindowsSpellingEngine : ISpellingEngine, IDisposable
{
    /// <summary>
    /// A ceiling on what one Suggest call will return, so a misbehaving enumerator cannot spin.
    /// The number actually shown is the caller's business — see AppSettings.SpellSuggestionCount.
    /// </summary>
    private const int SuggestionCeiling = 16;

    private readonly ILogger<WindowsSpellingEngine> _logger;
    private readonly Lock _sync = new();

    private ISpellChecker? _checker;
    private bool _attempted;
    private bool _available;
    private bool _disposed;

    public WindowsSpellingEngine(ILogger<WindowsSpellingEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
    }

    public bool IsAvailable
    {
        get
        {
            EnsureChecker();

            return _available;
        }
    }

    /// <summary>
    /// Creates the checker off the UI thread, and writes what it found to the log.
    ///
    /// Called once at startup. Two reasons: the first real call then costs nothing, and the
    /// checker is built on a pool thread, which is where every later call comes from.
    /// </summary>
    public Task WarmUpAsync() => Task.Run(EnsureChecker);

    public IReadOnlyList<SpellingRange> Check(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text.Length == 0)
        {
            return [];
        }

        EnsureChecker();

        lock (_sync)
        {
            if (_checker is null)
            {
                return [];
            }

            try
            {
                return ReadErrors(_checker, text);
            }
            catch (COMException ex)
            {
                // Checking is a convenience. A checker that has stopped answering must not take
                // typing down with it.
                _logger.LogWarning(ex, "The spell checker failed to check a line.");

                return [];
            }
        }
    }

    public IReadOnlyList<string> Suggest(string word)
    {
        ArgumentNullException.ThrowIfNull(word);

        if (word.Length == 0)
        {
            return [];
        }

        EnsureChecker();

        lock (_sync)
        {
            if (_checker is null)
            {
                return [];
            }

            try
            {
                _checker.Suggest(word, out ComTypes.IEnumString suggestions);

                return ReadStrings(suggestions, SuggestionCeiling);
            }
            catch (COMException ex)
            {
                _logger.LogWarning(ex, "The spell checker failed to suggest for {Word}.", word);

                return [];
            }
        }
    }

    // ------------------------------------------------------------------ creation

    /// <summary>
    /// Builds the checker on first use, once. A machine with no dictionary for the user's
    /// language is an ordinary outcome, not a failure: <see cref="_available"/> stays false, every
    /// call returns empty, and the preferences page greys the setting out with the reason.
    /// </summary>
    private void EnsureChecker()
    {
        lock (_sync)
        {
            if (_attempted || _disposed)
            {
                return;
            }

            _attempted = true;

            try
            {
                var factory = (ISpellCheckerFactory)new SpellCheckerFactoryRcw();

                try
                {
                    string? language = ChooseLanguage(factory);

                    if (language is null)
                    {
                        _logger.LogInformation(
                            "Spell checking is off: Windows has no dictionary installed for {Culture} or en-US.",
                            CultureInfo.CurrentUICulture.Name);

                        return;
                    }

                    factory.CreateSpellChecker(language, out ISpellChecker checker);

                    _checker = checker;
                    _available = true;

                    ReportReady(language);
                }
                finally
                {
                    Marshal.ReleaseComObject(factory);
                }
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or NotSupportedException)
            {
                // The service is absent or refused to start. Nothing here is worth an error: the
                // app works without it, and the log line is what explains the missing squiggles.
                _logger.LogInformation(ex, "Spell checking is off: the Windows spell service is unavailable.");
            }
        }
    }

    /// <summary>
    /// The user's own language if Windows can spell it, otherwise en-US, otherwise nothing.
    /// </summary>
    private static string? ChooseLanguage(ISpellCheckerFactory factory)
    {
        string current = CultureInfo.CurrentUICulture.Name;

        if (IsSupported(factory, current))
        {
            return current;
        }

        return IsSupported(factory, "en-US") ? "en-US" : null;
    }

    private static bool IsSupported(ISpellCheckerFactory factory, string language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return false;
        }

        factory.IsSupported(language, out int supported);

        return supported != 0;
    }

    /// <summary>
    /// One log line naming the language and what the first check and suggestion cost.
    ///
    /// This is the measurement the design rests on rather than idle curiosity. Check runs on the
    /// pool inside the render debounce, but Suggest runs on the UI thread while a context menu is
    /// opening, so its cost is the one a user would feel.
    /// </summary>
    private void ReportReady(string language)
    {
        if (_checker is null)
        {
            return;
        }

        try
        {
            var timer = Stopwatch.StartNew();
            int found = ReadErrors(_checker, SelfTestLine).Count;
            double checkMs = timer.Elapsed.TotalMilliseconds;

            timer.Restart();
            _checker.Suggest("recieve", out ComTypes.IEnumString suggestions);
            int offered = ReadStrings(suggestions, SuggestionCeiling).Count;
            double suggestMs = timer.Elapsed.TotalMilliseconds;

            _logger.LogInformation(
                "Spell checking is on. Language {Language}; self-test found {Found} error(s) in {CheckMs:F1} ms "
                + "and offered {Offered} suggestion(s) in {SuggestMs:F1} ms.",
                language,
                found,
                checkMs,
                offered,
                suggestMs);
        }
        catch (COMException ex)
        {
            _logger.LogInformation(ex, "Spell checking is on for {Language}, but the self-test did not run.", language);
        }
    }

    /// <summary>Two misspellings and a repeated word, so the self-test exercises both kinds.</summary>
    private const string SelfTestLine = "This sentance has has a mispelling in it.";

    // ------------------------------------------------------------------- reading

    private static List<SpellingRange> ReadErrors(ISpellChecker checker, string text)
    {
        // Check rather than ComprehensiveCheck, deliberately. The comprehensive form does work
        // that needs surrounding context, and the analyzer caches results per line - a key that
        // only stays valid while a line can be judged on its own. See docs/Spelling.md.
        checker.Check(text, out IEnumSpellingError errors);

        List<SpellingRange> found = [];

        try
        {
            while (errors.Next(out ISpellingError? error) == 0 && error is not null)
            {
                try
                {
                    error.GetStartIndex(out uint start);
                    error.GetLength(out uint length);
                    error.GetCorrectiveAction(out int action);

                    if (length > 0)
                    {
                        found.Add(new SpellingRange(
                            (int)start,
                            (int)length,
                            action == CorrectiveActionDelete
                                ? SpellingIssueKind.RepeatedWord
                                : SpellingIssueKind.Misspelling));
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(error);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(errors);
        }

        return found;
    }

    private static List<string> ReadStrings(ComTypes.IEnumString source, int ceiling)
    {
        List<string> words = [];

        try
        {
            string[] buffer = new string[1];

            while (words.Count < ceiling && source.Next(1, buffer, IntPtr.Zero) == 0)
            {
                if (!string.IsNullOrWhiteSpace(buffer[0]))
                {
                    words.Add(buffer[0]);
                }
            }
        }
        finally
        {
            Marshal.ReleaseComObject(source);
        }

        return words;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _available = false;

            if (_checker is not null)
            {
                Marshal.ReleaseComObject(_checker);
                _checker = null;
            }
        }
    }

    // ------------------------------------------------------------------- interop

    /// <summary>CORRECTIVE_ACTION_DELETE: the word should go, which is how a repeat is reported.</summary>
    private const int CorrectiveActionDelete = 3;

    /// <summary>
    /// "Microsoft Spell Checker Factory Class", C:\Windows\System32\MsSpellCheckingFacility.dll,
    /// registered ThreadingModel=Free - which is why every call below can come from the thread
    /// pool without a marshalling proxy in the way.
    /// </summary>
    [ComImport, Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC")]
    private class SpellCheckerFactoryRcw;

    /// <summary>
    /// The IID was recovered from the running system rather than taken from a header: no Windows
    /// SDK is installed here, and these interfaces are not registered under HKCR\Interface. The
    /// factory object was created, every GUID-shaped run of bytes in MsSpellCheckingFacility.dll
    /// was collected, and each was offered to QueryInterface until one was accepted. Worth
    /// knowing if another interface in this family is ever needed.
    /// </summary>
    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        void GetSupportedLanguages(out ComTypes.IEnumString value);

        void IsSupported([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out int value);

        void CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string languageTag, out ISpellChecker value);
    }

    /// <summary>
    /// Declared in full because COM interop reads the vtable by position, so every member has to
    /// be present and in order even where this app never calls it. The two that take types not
    /// declared here are given IntPtr, which keeps the slot the right width.
    /// </summary>
    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        void GetLanguageTag([MarshalAs(UnmanagedType.LPWStr)] out string value);

        void Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);

        void Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out ComTypes.IEnumString value);

        void Add([MarshalAs(UnmanagedType.LPWStr)] string word);

        void Ignore([MarshalAs(UnmanagedType.LPWStr)] string word);

        void AutoCorrect(
            [MarshalAs(UnmanagedType.LPWStr)] string from,
            [MarshalAs(UnmanagedType.LPWStr)] string to);

        void GetOptionValue([MarshalAs(UnmanagedType.LPWStr)] string optionId, out byte value);

        void GetOptionIds(out ComTypes.IEnumString value);

        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string value);

        void GetLocalizedName([MarshalAs(UnmanagedType.LPWStr)] out string value);

        void AddSpellCheckerChanged(IntPtr handler, out uint eventCookie);

        void RemoveSpellCheckerChanged(uint eventCookie);

        void GetOptionDescription([MarshalAs(UnmanagedType.LPWStr)] string optionId, out IntPtr value);

        void ComprehensiveCheck([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingError value);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        // PreserveSig because the end of the enumeration is S_FALSE, which is a success code:
        // letting the marshaller check it would leave "no more errors" indistinguishable from
        // "one more error" without inspecting the out parameter.
        [PreserveSig] int Next(out ISpellingError? value);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
        void GetStartIndex(out uint value);

        void GetLength(out uint value);

        void GetCorrectiveAction(out int value);

        void GetReplacement([MarshalAs(UnmanagedType.LPWStr)] out string value);
    }
}
