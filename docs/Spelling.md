# Spell checking

How the spell checker is put together, what the seam to the engine is, and what a different
engine would have to provide. Written because the seam is the part that is not obvious from the
code: the rest is ordinary.

---

## The shape

```
Markdown            MarkdownRegionScanner, LineMasker      pure text, no state
   ^
Spelling            SpellingAnalyzer, WordPolicy, SeedDictionary
   |                       ^                    ^
   |                 ISpellingEngine      IUserDictionary
   |                       ^                    ^
   +-- App ------- WindowsSpellingEngine        |
       Services ---------------------- UserDictionaryService
```

Two things arrive from outside. **The engine** knows how to spell and belongs to the app layer,
because the only implementation is Windows COM. **The word list** is a file and belongs to the
services layer. `AddMarqoraSpelling()` registers neither, deliberately — see *Registration* below.

The analyzer returns `SpellingIssue`, not `Diagnostic`. Nothing beneath the app layer knows what
Monaco is; the mapping to a decoration happens at the bridge.

---

## Mask, do not tokenise

`ISpellChecker::Check` takes a whole line and returns the runs it objects to. Windows does the
word-splitting — contractions, possessives, hyphenation, locale rules — and it does it far better
than a hand-rolled tokenizer would.

So the work here is not splitting words. It is **blanking the runs of a line that are not prose**
and handing over what is left: `LineMasker` for the inline things, `MarkdownRegionScanner` for the
whole-line ones.

Two rules govern it, and both have drawn blood:

**A mask overwrites in place and never deletes.** Every offset the engine returns is an offset
into the line it was given, and the caller reads the word back out of the *original*. A mask that
changed the length would put every squiggle in the wrong column.

**A mask fills with a separator, not spaces.** A space says nothing was ever there, which joins
the words either side of it. ``and `^` and`` became `and⎵⎵⎵⎵⎵and`, and the engine correctly
reported a word typed twice — reading across a code span it could not see.

### `Check`, never `ComprehensiveCheck`

The comprehensive form does work that needs surrounding context. Two reasons it is not used:

- **The cache depends on it.** Results are keyed by the line's own text. That key is only valid
  while a line can be judged on its own.
- **Paragraphs wrap across source lines** here, because of the wrap-column preference, so the
  context a comprehensive check wants is not in the string it would be given anyway.

---

## The seam

```csharp
public interface ISpellingEngine
{
    bool IsAvailable { get; }
    IReadOnlyList<SpellingRange> Check(string text);
    IReadOnlyList<string> Suggest(string word);
}
```

**This contract is line-shaped, and that is not neutral.** It mirrors the Windows API. An engine
that can only judge one word at a time — Hunspell, for instance — must carry its own tokenizer
inside the adapter to satisfy it. Swapping engines is therefore not a uniform-cost operation, and
that is a deliberate trade rather than an oversight: the masking is shared, the word-splitting is
not.

A second engine is not planned. The seam exists so the surface area is known if that changes.

### What a new engine must provide

| Member | Contract |
|---|---|
| `IsAvailable` | False is ordinary, not a failure. Every other member returns empty while it is false, the feature switches itself off, and the preferences checkbox greys out with the reason |
| `Check` | Offsets into the string it was handed. Callers pass a **masked** line, so those offsets are valid against the original only because masking preserves length |
| `Suggest` | Best first, or empty. Called **on the UI thread** while a menu is opening, so it must be quick — the analyzer caches by word, but the first call for each word is felt |

Called from the thread pool. Be safe to call concurrently, or serialize internally.

### What is Windows-specific

- **Repeated-word detection.** Windows reports it through `CORRECTIVE_ACTION_DELETE`. An engine
  that cannot find repeats simply never emits `SpellingIssueKind.RepeatedWord`, and the feature
  degrades rather than breaking — which is why the kind exists at all.
- **The language model.** Windows takes a BCP-47 tag and answers `IsSupported`. Hunspell takes
  `.dic` and `.aff` file paths. There is no common shape, so language selection would have to be
  designed per engine.

### Recovering the COM identity

No Windows SDK is installed on the development machine, and these interfaces are not registered
under `HKCR\Interface`, so the IIDs could not be looked up. They were recovered from the running
system: create the factory, collect every GUID-shaped run of bytes in
`C:\Windows\System32\MsSpellCheckingFacility.dll`, and offer each to `QueryInterface` until one is
accepted. Worth knowing if another interface in the family is ever needed.

`ISpellCheckerFactory` is `8E018A9D-2415-4677-BF08-794EA61F94BB`. The class is registered
`ThreadingModel=Free`, which is why a thread-pool caller gets the object itself rather than a
marshalling proxy.

---

## What is never checked

| Kept out by | What |
|---|---|
| `MarkdownRegionScanner` | Fenced code, YAML front matter, four-space indented code |
| `LineMasker` | Inline code, link and image targets, autolinks and bare URLs, HTML tags, maths, footnote markers, entities, emoji shortcodes |
| `WordPolicy` | ALL-CAPS acronyms, anything containing a digit, camelCase and PascalCase identifiers |
| `SeedDictionary` | Around sixty words Marqora ships knowing — its own name, its vocabulary, and the software prose it is written in |
| `IUserDictionary` | Whatever the user has accepted, including when the flagged token wears a possessive |

Link *text* and reference *titles* are checked. A reader sees them.

**The seed list is load-bearing, not a convenience.** "Marqora" is a single capitalised word with
no interior capital, so no skip rule exempts it and no Windows dictionary contains it. It appears
21 times in the welcome document — the first thing anyone opens. Without the list, a first run
greets every new user with two dozen red underlines, most of them under the product's own name.

---

## Cost, and the cache

Measured on the development machine: **`Check` about 280 µs per line**, **`Suggest` about 3.2 ms
per word**, both warm.

That makes the cache load-bearing rather than an optimisation. A five-thousand-line document is
roughly a second and a half for a full sweep; a keystroke is one line.

The cache is keyed on **the line's own text**, so a line that moves down ten rows is still a hit.
It holds **raw engine output — before** the skip rules, the seed list and the user's list are
applied. Adding a word therefore invalidates nothing: the next pass replays the same cached ranges
through a filter that now excludes it, with no calls to the engine at all. That is what makes
*Add to Dictionary* feel instant on a large document.

**Held in reserve, not built:** batching contiguous runs of cache misses into a single `Check`
call, which would turn a first open into one COM call instead of thousands. Contiguous only —
joining non-adjacent lines would invent repeated-word pairs across the seam.

---

## Registration

```csharp
services.AddMarqoraSpelling();   // the analyzer, and nothing else
```

`ISpellingEngine` and `IUserDictionary` are **not** registered there, and there is no null-object
fallback for either.

There used to be one for the word list, on the reasoning that the library should work standalone.
It silently won: `TryAdd` keeps the first registration, this layer is registered before the one
supplying the real list, and the analyzer spent its life filtering against a list that never
learned a word — *Add to Dictionary* appeared to do nothing while writing the file correctly the
whole time.

A null object indistinguishable from a working implementation is worse than no registration.
Missing now means a loud failure at startup naming the interface.
`SpellingRegistrationTests` pins it.
