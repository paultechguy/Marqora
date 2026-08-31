---

## Download

**[{{ZIP}}]({{DOWNLOAD_URL}})** — Windows 10 1809 (build 17763) or later, 64-bit.

Self-contained in every sense. The .NET runtime and the Windows App SDK are inside the zip, so
the target machine needs neither. The installer is per-user and writes nothing outside `HKCU`
and your own profile, so it needs no administrator. And Marqora makes no network calls at
runtime, so it needs no connection.

## Install

1. Extract the zip into a folder of its own.
2. Double-click `Install.cmd`.
3. Marqora is in your Start menu and on your desktop.

The app installs to `%LOCALAPPDATA%\Programs\Marqora`. You also need the WebView2 runtime,
which Windows 11 already has and Windows 10 gets through Windows Update; the installer checks
for it and tells you where to get it if it is missing.

To remove it: **Settings → Apps → Installed apps → Marqora → Uninstall**, or `Uninstall.cmd`
from the extracted folder. Your settings, recent files and snippets under
`%LOCALAPPDATA%\PaulTechGuy\Marqora` are kept either way, so installing a newer version does
not lose them. `Uninstall.cmd -RemoveUserData` removes those too.

## About the security warnings

Marqora is not code-signed — a certificate costs several hundred dollars a year, which is hard
to justify for a free tool. Windows therefore treats it the way it treats anything else it has
not seen before:

- Explorer may show **Open File - Security Warning** when you run `Install.cmd`. Click **Run**.
- Running `Marqora.exe` straight out of the folder without installing shows the blue
  **Windows protected your PC** panel. Click **More info**, then **Run anyway**.

The installer strips the downloaded-file marker from the installed copy, so once Marqora is
installed it starts normally with no warnings at all.

## Verify your download

```
SHA256  {{SHA256}}
```

```powershell
Get-FileHash {{ZIP}} -Algorithm SHA256
```

A mismatch means the copy is damaged rather than dangerous — 84 MB across a network or a USB
stick does occasionally arrive short, and a partly corrupt zip will often extract far enough
to look right and then fail somewhere later, which is a miserable thing to diagnose. Copy it
across again rather than installing what you have.

What this proves is that the file arrived exactly as it was built. It is not a signature and
says nothing about who built it: anyone able to alter the zip could rewrite the checksum
beside it. Only a code-signing certificate answers that question, and Marqora does not have
one.
