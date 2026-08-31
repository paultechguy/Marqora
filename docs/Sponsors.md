# Support the project

Where the GitHub links live, and what has to be true outside the repository for the
**Help → Support the Project** menu item to lead anywhere.

The feature is deliberately small: a menu item, a dialog with three buttons, and two URLs
handed to the shell. Nothing is fetched, nothing is authenticated, and nothing about who
clicked what is recorded — the About box states outright that Marqora makes no network calls
and carries no telemetry, and that has to stay true.

---

## The URLs

`src/PaulTechGuy.MQ.Domain/ProjectLinks.cs` is the only place any of them is written down.

| Constant | Value |
|----------|-------|
| `RepositoryUrl` | `https://github.com/paultechguy/Marqora` |
| `SponsorsUrl` | `https://github.com/sponsors/paultechguy` |
| `LicenceUrl` | `RepositoryUrl` + `/blob/master/LICENSE` |

They are constants in Domain rather than settings in a config file. These change when the
repository moves, which is to say never, and a settings file would mean shipping something a
user can break and the app must then validate for no gain. `LicenceUrl` is built from
`RepositoryUrl` so moving the repository cannot leave the About box pointing at the old one.

Three consumers: the Support dialog, the About box's `License` row, and the `Copy details`
text the About box puts on the clipboard.

---

## What the dialog does when a URL is missing

`ProjectLinks.IsUsable` decides whether a button appears at all. It accepts only absolute
`http`/`https` addresses, so:

- **A URL is emptied or mistyped.** Its button is not created. The dialog still opens and
  still says what it has to say; the other button still works.
- **Both are unusable.** The dialog is text and a `Close` button. Nothing throws.
- **The scheme is anything else.** Refused before it reaches the shell. Handing an arbitrary
  URI to `Launcher` hands it to whichever application claims that scheme, and none of these
  links has any business doing that.

Emptying `SponsorsUrl` is therefore also the way to switch the Sponsor button off without
touching any other code.

`ExternalLink.OpenAsync` is what actually launches. It returns the `bool` that
`LaunchUriAsync` reports rather than dropping it — a shell that declines to launch says so by
returning false, not by throwing — and `MainWindow.ShowSupportAsync` turns a false into a
plain message naming the URL, so it can be read off and typed in by hand.

---

## FUNDING.yml

`.github/FUNDING.yml` puts GitHub's own **Sponsor** button on the repository page. It is
separate from the app's menu item and needs nothing from the code.

It does nothing until the Sponsors profile is approved and public. That is expected, not a
misconfiguration.

---

## When the link 404s

The Sponsors URL is populated and the menu item ships enabled, so until GitHub approves the
Sponsors profile for `paultechguy` the button leads to a 404. Nothing in the repository can
fix that; the profile has to be enrolled, completed, submitted and approved on GitHub.

Once the public Sponsors page resolves, the existing build starts working with no change
here.

---

## What is deliberately absent

No Sponsors API, no sponsor recognition or badges, no sponsor-only features, no webhook, no
analytics on whether the button was clicked, and no second funding provider. Each of those
would give the app a reason to care whether someone sponsored, and it should not have one.
