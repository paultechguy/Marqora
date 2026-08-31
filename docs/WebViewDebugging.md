# Debugging the preview WebView

Both panes live inside one WebView2 page, so a fair number of bugs that look like C# bugs
are really page bugs, and the other way round. A single question asked in the browser's
own console usually settles which side to read - and settles it in seconds, where reading
the bridge from either end can take an afternoon and still be a guess.

This document is how to get that console, because in Marqora it is not where you would
expect it.

---

## Why F12 and right-click do nothing

`WebViewPreviewHost.AttachAsync` turns off two settings deliberately:

| Setting | Why it is off |
|---------|---------------|
| `AreDefaultContextMenusEnabled` | Chromium's menu offered Back, Reload, Save as and Inspect - none of them meaningful for a document - and it was drawn by Edge, so it followed Edge's dark mode rather than Marqora's theme. The page still sees the click and the host raises a WinUI flyout instead. |
| `AreBrowserAcceleratorKeysEnabled` | The browser would otherwise swallow accelerators the editor needs. |

`AreDevToolsEnabled` is true in Debug builds, but with the menu and the accelerator keys
gone there is no way in from the app itself. Neither setting is worth turning back on for
a debugging session: the fix is to attach from outside.

## Setting it up, once

Create `src/PaulTechGuy.MQ.App/Properties/launchSettings.json`:

```json
{
  "profiles": {
    "PaulTechGuy.MQ.App": {
      "commandName": "Project",
      "environmentVariables": {
        "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS": "--remote-debugging-port=9222"
      }
    }
  }
}
```

The file is in `.gitignore`: the port is a local choice and the file is per developer.
WebView2 reads the variable when it creates its environment, so it has to be set before
the app starts - setting it later, or attaching to an already-running app, does nothing.

Then, once per machine, in Microsoft Edge: open `edge://inspect`, click **Configure…**,
add `localhost:9222`, and close the dialog. Edge remembers it.

## Using it

1. Run Marqora from Visual Studio (Debug, the profile above).
2. In Edge, open `edge://inspect`. The shell page is listed under *Remote Target*.
3. Click **inspect**. That is the full Chromium DevTools - Console, Elements, Network,
   Sources - attached to the preview.

The pop-out windows are separate pages and appear in the same list: the cheatsheet, each
open diagram window. Pick the one whose title matches what you are debugging.

### Things worth knowing

- **The element picker selects the whole body.** An overlay sits over the panes and takes
  the hover. Query the DOM from the Console instead; it is faster anyway.
- **`fetch` cannot reach the document origin.** `shell.html`'s content security policy
  allows `connect-src 'self' blob: data:` only, so a `fetch` test against
  `https://marqora.document/...` is blocked by policy and tells you nothing about whether
  the file is reachable. Test with an `Image` instead, which `img-src` permits.
- **Reloading the page loses the tabs.** `location.reload()` brings the shell back empty,
  because the host does not know to resend the documents. Restart the app after one.
- **Debug-level log lines need the built config.** `appsettings.json` ships at
  `Information`; edit the copy in `bin\Debug\...\win-x64\` to `"Default": "Debug"` for a
  run. A rebuild copies the source file back over it.

## The question to ask first

For anything about images, links or media in the preview, this names what the page
actually holds and whether the browser could load it:

```js
[...document.querySelectorAll('img')].map(i =>
  i.getAttribute('src') + '  →  ' + (i.complete && i.naturalWidth === 0 ? 'BROKEN' : 'ok'))
```

A `src` that is still relative means the shell did not rewrite it -
`rewriteRelativeUrls` in `webshell/app.js`. A rewritten `https://marqora.document/...`
that is `BROKEN` means the host did not serve it - `OnWebResourceRequested` in
`WebViewPreviewHost.cs`. The Network tab then says which: a 404 is a path that does not
exist, `ERR_NAME_NOT_RESOLVED` is nothing listening on that origin at all.

That second case is worth recognising on sight. Relative images were broken from the first
release because the document's folder was published with
`SetVirtualHostNameToFolderMapping`, and WebView2 hands those mappings to a page **when it
navigates**. The shell navigates once, at startup; the folder changes on every tab switch,
so the page was never told about any of them. It is now served per request instead, which
is also why the mapping call is gone from `SetDocumentLocation`.
