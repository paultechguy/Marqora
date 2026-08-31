Marqora {{VERSION}}
A Markdown viewer and editor for Windows.

===========================================================================
INSTALL
===========================================================================

  1. Extract this whole zip into a folder of its own. Keep the folder
     structure.
  2. Double-click Install.cmd.
  3. That's it. Marqora is in your Start menu and on your desktop.

Nothing is installed for other users, nothing needs an administrator, and
you will not be asked for a password. The app goes in your own profile at:

  %LOCALAPPDATA%\Programs\Marqora

You can delete this extracted folder once the install has finished.


---------------------------------------------------------------------------
Opening markdown files by double-click
---------------------------------------------------------------------------

The installer registers Marqora for .md, .markdown, .mdown, .mkd and .mdx,
but Windows does not allow any application to make itself the default for a
file type - that choice is sealed with a per-user hash only the Settings UI
can produce. So there is one manual step, once:

  right-click any .md file
    > Open with
    > Choose another app
    > Marqora
    > Always

After that, double-clicking a markdown file opens it in Marqora, and
selecting a dozen of them opens a dozen tabs in one window.


---------------------------------------------------------------------------
Requirements
---------------------------------------------------------------------------

  * Windows 10 1809 (build 17763) or later. Windows 11 recommended.
  * 64-bit (x64).
  * The WebView2 runtime, which Windows 11 already has and Windows 10 gets
    through Windows Update. The installer checks and tells you where to get
    it if it is missing.

You do NOT need .NET installed. This build carries its own copy.


---------------------------------------------------------------------------
About the security warnings
---------------------------------------------------------------------------

Marqora is not code-signed - a certificate costs several hundred dollars a
year, which is hard to justify for a free tool. Windows therefore treats it
as it treats anything else it has not seen before:

  * Explorer may show "Open File - Security Warning" when you run
    Install.cmd. Click Run.

  * If you run Marqora.exe straight out of this folder without installing,
    SmartScreen shows a blue "Windows protected your PC" panel. Click
    "More info", then "Run anyway".

The installer removes the downloaded-file marker from the installed copy, so
once Marqora is installed it starts normally with no warnings at all. Every
file here came out of the build in the project's repository, and the app
makes no network calls at runtime.


---------------------------------------------------------------------------
Checking that the download arrived intact
---------------------------------------------------------------------------

The build writes a SHA256 checksum beside the zip, in a file named

  Marqora-{{VERSION}}-win-x64.zip.sha256

It is not inside the zip - a checksum of a zip cannot live within the zip it
describes - so you have it only if it was sent along with the download. If
you do, open PowerShell in the folder holding the zip and run:

  Get-FileHash Marqora-{{VERSION}}-win-x64.zip -Algorithm SHA256

The Hash it prints should match the one in the .sha256 file, ignoring upper
and lower case.

A mismatch means the copy is damaged rather than dangerous. 84 MB across a
USB stick, a network share or a mail server does occasionally arrive short,
and a partly corrupt zip will often extract far enough to look right and
then fail somewhere later, which is a miserable thing to diagnose. Copy it
across again rather than installing what you have.

What this proves is that the file arrived exactly as it was built. It is not
a signature and says nothing about who built it: anyone able to alter the
zip could rewrite the checksum sitting beside it. Only a code-signing
certificate answers that question, and Marqora does not have one.


===========================================================================
UNINSTALL
===========================================================================

Either of these:

  * Settings > Apps > Installed apps > Marqora > Uninstall
  * Double-click Uninstall.cmd in this folder

Both remove the app, the shortcuts, the file associations and the Settings
entry.

Your settings, recent files list and custom snippets are KEPT, so that
installing a newer version does not lose them. They live in:

  %LOCALAPPDATA%\PaulTechGuy\Marqora

To remove those too, either delete that folder afterwards, or run:

  Uninstall.cmd -RemoveUserData


===========================================================================
OPTIONS
===========================================================================

Install.cmd and Uninstall.cmd pass anything you give them to the scripts in
install\, so from a command prompt:

  Install.cmd -NoDesktopShortcut        skip the desktop shortcut
  Install.cmd -NoStartMenuShortcut      skip the Start menu shortcut
  Install.cmd -NoFileAssociations       do not register markdown file types
  Install.cmd -InstallDir "D:\Apps\Marqora"
                                        install somewhere else
  Install.cmd -Force                    close Marqora if it is running
  Install.cmd -Quiet                    no progress output

  Uninstall.cmd -RemoveUserData         also delete settings and snippets
  Uninstall.cmd -Force                  close Marqora if it is running

An upgrade is just an install: run Install.cmd from the newer release and it
replaces what is there, keeping your settings.


===========================================================================
Copyright (c) Paul Carver
===========================================================================
