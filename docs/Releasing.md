# Releasing Marqora

A release is two commands with your own judgement in between, and a click at the end.

```powershell
pwsh .\build\New-ReleaseNotes.ps1 -Version 0.3.0   # bump + scaffold the notes
#   ... write the notes, commit them to dev ...
pwsh .\build\Publish-Release.ps1 -Version 0.3.0    # promote, build, tag, draft
#   ... smoke-test the draft, click Publish release ...
pwsh .\build\Publish-Release.ps1 -Version 0.3.0 -Verify
```

Nothing in that sequence is unrecoverable, which is deliberate and worth knowing before you
start. The rest of this document explains what each part does and why it is shaped that way.

---

## What "ready to release" means

`Publish-Release.ps1` assumes `dev` is already the thing you want to ship. Specifically:

- The version in `Directory.Build.props` is the version you are releasing.
- `docs/releases/v<version>.md` exists, is written, and is committed.
- Both are pushed, and your working tree is clean.

That is not a state some earlier script left behind — it is a plain fact about `dev`, which
you can check with `git log`. The release notes get there the same way every other change
does: you write them, you commit them, you push them.

**This is the main design decision in the whole pipeline.** An earlier draft had a script
prepare the release, pause for review, and then finish the job — which put an irreversible
push in the middle of a script run and made "did that release start?" a question you could
not answer by looking at the repository. Making the notes an ordinary commit removes the
question entirely.

---

## Step 1 — Scaffold the notes and bump the version

```powershell
pwsh .\build\New-ReleaseNotes.ps1 -Version 0.3.0
```

Changes exactly two things in your working tree and stops:

```
Directory.Build.props        <Version> set to 0.3.0
docs/releases/v0.3.0.md      scaffolded from build/release-notes-template.md
```

Nothing is committed. Nothing is pushed. If you change your mind, the script tells you the
two commands that undo it.

The gates here are the cheap ones — branch, tree, ancestry, and whether the version is still
free. Scaffolding a markdown file has no business running the test suite.

`-Check` runs those gates and stops, which answers "could I release from here?" without
starting anything. `-Force` overwrites notes you have already started.

## Step 2 — Write the notes and commit them

Fill in the placeholders. Delete any heading with nothing under it: an empty **Fixed**
section reads worse than no **Fixed** section.

Write for someone deciding whether to download this, not for someone reading the diff. The
install steps, the download link and the checksum are added automatically when the release is
published, so do not repeat them.

```powershell
git add Directory.Build.props docs/releases/v0.3.0.md
git commit -m "Release notes for 0.3.0"
git push origin dev
```

Take as long as you like. Nothing is waiting on you.

## Step 3 — Publish

```powershell
pwsh .\build\Publish-Release.ps1 -Version 0.3.0
```

Runs every gate, shows you exactly what it is about to do, and asks once. Then:

```
  [1/5] promote master     git merge --ff-only dev, then push
  [2/5] build from master  New-Release.ps1 -Test
  [3/5] tag                annotated v0.3.0, then push
  [4/5] release body       your notes plus the generated footer
  [5/5] draft              gh release create --draft, both assets attached
```

**This script writes no commits.** It promotes, builds, tags and uploads; that is all. It
returns you to `dev` on the way out, including when something fails.

`-WhatIf` runs all nine gates for real and then prints the git and `gh` commands without
running them. `-Yes` skips the confirmation for an unattended run.

## Step 4 — Smoke-test the draft, then publish it

The draft is private. Its asset is the literal file GitHub will serve, which is why the test
happens here rather than against a zip you built locally five minutes earlier:

```powershell
gh release download v0.3.0 --dir $env:TEMP\marqora-0.3.0
#   extract it, run Install.cmd, launch, check Help > About says 0.3.0
```

Happy with it? Click **Publish release** on the draft page. Not happy? The script printed the
two commands that delete the draft and the tag.

## Step 5 — Confirm what shipped

```powershell
pwsh .\build\Publish-Release.ps1 -Version 0.3.0 -Verify
```

Downloads both assets, checks the zip against its published checksum, and confirms the
archive's root holds the five entries it should.

---

## The gates

All nine run in `Publish-Release.ps1`; the first four also run in `New-ReleaseNotes.ps1`.
Every one is read-only, which is what makes it safe to run them before deciding whether a
release should happen at all.

| Gate | Refuses when |
|---|---|
| tooling | `gh` is missing or not signed in |
| branch and tree | You are not on `dev`, the tree is dirty, or `dev` and `origin/dev` disagree |
| `master` ancestor of `dev` | `master` has commits `dev` does not — the fast-forward would fail |
| tag and release | `v<version>` exists locally, on origin, or as a GitHub release; or the version does not come after the newest tag |
| version | `Directory.Build.props` disagrees with `-Version` |
| release notes | Missing, still carrying placeholders, or saying nothing the template did not |
| tests | `dotnet test` fails |
| licence headers | `Add-FileHeaders.ps1 -Check` fails |
| build, no warnings | `dotnet build -warnaserror` fails |

Two are worth explaining.

**`master` ancestor of `dev`** is the one that would hurt most if it were missing. The
repository ruleset blocks force-pushing and cannot be bypassed by anyone, so if `master` has
drifted there is no quick way back — the fix is to merge `master` into `dev` and release from
the result. Far better to learn that before the version is bumped than halfway through a
release.

**version** looks like a formality and is not. It catches the two mistakes people actually
make: releasing while the bump commit is still sitting unpushed, and typing last release's
number out of habit.

---

## Why the build comes from `master`

`master` is where releases are tagged, so the shipped artifact is built there too. After the
fast-forward `master` and `dev` are the same commit, so the tree is identical either way —
building where you tag simply means the artifact and the tag cannot drift, even in principle.

The `--ff-only` is a choice rather than a requirement. The ruleset blocks *force* pushes; it
would happily accept a merge commit on `master`. Fast-forward is what `CONTRIBUTING.md`
promises ("`master` … only ever moves forward from `dev`"), and it is what makes the sentence
above true.

---

## The two templates

Release notes are assembled from two files, because **the checksum cannot exist before the
build that produces it**.

| File | Rendered | Ends up |
|---|---|---|
| `build/release-notes-template.md` | Step 1 | Committed at `docs/releases/v<version>.md` |
| `build/release-footer-template.md` | Step 3 | Appended to the release body; never committed |

Your notes are committed in step 2. The zip is not built until step 3. Committing a trial
build's hash would be wrong — .NET builds are not bit-reproducible, so a local rebuild hashes
differently — and committing a literal `{{SHA256}}` would leave a placeholder in the
repository forever. So the checksum, the download link and the install boilerplate live only
in the footer, which is rendered at publish time and written to
`build/artifacts/release-body-<version>.md`.

`{{TOKEN}}` is the same idiom `build/installer/README.txt` already uses for its version, done
the same way: a plain string replace. `Expand-Template` refuses to return text with tokens
still in it, so a typo in a token name fails loudly rather than shipping `{{SHA256}}` to the
release page.

There is deliberately no `{{DATE}}`. Notes may be written days before the release, so a
scaffold-time date would be wrong by the time you publish, and the release page carries its
own date anyway.

---

## If something goes wrong

| Reached | How to undo |
|---|---|
| Step 1 finished | `git checkout -- Directory.Build.props`, delete the notes file |
| Step 2 committed and pushed | An ordinary commit. Revert it with another ordinary commit. |
| `master` fast-forwarded | Moves `master` to a commit already on `dev`. Harmless on its own. |
| Tag pushed | `git push --delete origin v0.3.0` then `git tag -d v0.3.0` |
| Draft created | `gh release delete v0.3.0 --yes` |
| Published | `gh release delete` removes the page, though watchers were already notified |

Tags sit outside the ruleset, so they can always be deleted. Branch commits cannot be
rewritten — but the worst thing a wrong one says is "Release notes for 0.3.0" for a release
you decided not to make, which a revert commit tidies up like any other mistake.

---

## What is not automated

- **Building in CI.** Both scripts are CI-shaped already — exit codes, `-Yes`, no prompts —
  so moving the publish into a GitHub Actions workflow later is a small step. The reason to
  do it is reproducible provenance, not ceremony: today the artifact's provenance is "the
  machine Paul ran this on".
- **Provenance attestation.** `gh attestation` is meaningful only for artifacts built in CI,
  so it follows the point above rather than leading it.
- **A winget manifest.** Needs a stable asset URL and a SHA256, which is exactly what this
  pipeline now produces. A sensible follow-up once a release exists.
- **Code signing.** There is no certificate, and there is no plan for one at several hundred
  dollars a year. `build/installer/README.txt` and the release footer both explain the
  SmartScreen consequence honestly rather than pretending it away.
