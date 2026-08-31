#Requires -Version 5.1

<#
.SYNOPSIS
    Removes a per-user Marqora install.

.DESCRIPTION
    Undoes everything Install.ps1 created: the shortcuts, the markdown file associations,
    the Settings > Apps entry, and the install directory itself. What it removes is read
    from the manifest the installer wrote, so an install that skipped the desktop shortcut
    or went to a non-default directory uninstalls correctly rather than by guesswork.

    User data - settings, recent files, snippets and logs under
    %LOCALAPPDATA%\PaulTechGuy\Marqora - is deliberately left alone unless -RemoveUserData
    is given. An upgrade is an uninstall followed by an install often enough that silently
    deleting hand-written snippets would be a bad default, and the script prints where the
    data is so removing it stays a one-line follow-up.

    The script can be started from inside the directory it is about to delete, which is what
    the Uninstall button in Settings > Apps does. It handles that by copying itself to the
    temp directory and re-running from there - see the relaunch below. A .ps1 is read into
    memory before it executes, so deleting the script file mid-run is harmless; the real
    problem is any process holding the directory open as its working directory, and moving
    out of the tree entirely sidesteps every variant of that.

.PARAMETER InstallDir
    The directory to remove. Found from the manifest, then the Settings > Apps entry, then
    the default location, so this is rarely needed.

.PARAMETER RemoveUserData
    Also deletes %LOCALAPPDATA%\PaulTechGuy\Marqora. This is not recoverable: custom
    snippets and settings go with it.

.PARAMETER Force
    Closes a running Marqora that has not responded to a polite request to shut down. A
    running Marqora is always asked to close first, with or without this.

.PARAMETER Quiet
    Suppresses progress output and never pauses. This is what the Settings > Apps entry
    uses for its quiet uninstall path.

.PARAMETER NoPause
    Returns immediately instead of waiting for a key. Uninstall.cmd passes this because it
    does its own pause; the Uninstall button in Settings has no wrapper, so the default is
    to wait and let the user read what happened.

.PARAMETER FromTemp
    Internal. Set on the relaunched copy to stop it relaunching again.

.EXAMPLE
    .\Uninstall.cmd

    Removes the app, keeping settings and snippets.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install\Uninstall.ps1 -RemoveUserData

    Removes the app and everything it ever wrote.

.NOTES
    Exit codes: 0 success, 1 failure.
#>

[CmdletBinding()]
param(
    [string] $InstallDir,

    [switch] $RemoveUserData,

    [switch] $Force,

    [switch] $Quiet,

    [switch] $NoPause,

    [switch] $FromTemp
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$appName = 'Marqora'
$exeName = 'Marqora.exe'
$processName = 'Marqora'
$progId = 'PaulTechGuy.Marqora.Markdown'
$arpKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Marqora'
$dataDirectory = Join-Path $env:LOCALAPPDATA 'PaulTechGuy\Marqora'
$manifestName = 'install-manifest.json'
$uninstallFolder = 'uninstall'
$defaultExtensions = @('.md', '.markdown', '.mdown', '.mkd', '.mdx')

function Write-Step {
    param([string] $Message)

    if (-not $Quiet) {
        Write-Host $Message -ForegroundColor Cyan
    }
}

function Write-Detail {
    param([string] $Message)

    if (-not $Quiet) {
        Write-Host "  $Message" -ForegroundColor DarkGray
    }
}

function Write-Plain {
    param([string] $Message = '')

    if (-not $Quiet) {
        Write-Host $Message
    }
}

function Get-ManifestValue {
    param(
        $Manifest,
        [string] $Name,
        $Default = $null
    )

    # ConvertFrom-Json produces a PSCustomObject, and under Set-StrictMode reading a
    # property that is not there is an error rather than $null. A manifest written by an
    # older installer is a normal thing to meet, so every read goes through here.
    if ($null -ne $Manifest -and $Manifest.PSObject.Properties.Match($Name).Count -gt 0) {
        return $Manifest.$Name
    }

    return $Default
}

function Find-InstallDirectory {
    param([string] $Candidate)

    if ($Candidate) {
        return [System.IO.Path]::GetFullPath($Candidate)
    }

    # This script may be sitting inside the install (the Settings > Apps path) or beside
    # the release payload (the extracted-zip path). The first case answers itself.
    $parent = Split-Path -Parent $PSScriptRoot

    if ($parent -and (Test-Path -LiteralPath (Join-Path $parent $exeName) -PathType Leaf)) {
        return $parent
    }

    try {
        $recorded = (Get-ItemProperty -LiteralPath $arpKey -Name 'InstallLocation' -ErrorAction Stop).InstallLocation

        if ($recorded) {
            return $recorded
        }
    }
    catch {
        # No entry in Settings > Apps. Fall through to the default location.
    }

    return (Join-Path $env:LOCALAPPDATA "Programs\$appName")
}

function Read-Manifest {
    param([string] $Directory)

    $path = Join-Path $Directory "$uninstallFolder\$manifestName"

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    }
    catch {
        Write-Warning "The install manifest could not be read; falling back to the default locations. ($($_.Exception.Message))"
        return $null
    }
}

function Invoke-Relaunch {
    param([string] $Directory)

    $stage = Join-Path $env:TEMP ("Marqora-uninstall-" + [Guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $PSScriptRoot -Destination $stage -Recurse -Force

    $psExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'

    $arguments = @(
        '-NoProfile'
        '-ExecutionPolicy', 'Bypass'
        '-File', (Join-Path $stage 'Uninstall.ps1')
        '-FromTemp'
        '-InstallDir', $Directory
    )

    if ($RemoveUserData) { $arguments += '-RemoveUserData' }
    if ($Force) { $arguments += '-Force' }
    if ($Quiet) { $arguments += '-Quiet' }
    if ($NoPause) { $arguments += '-NoPause' }

    # Same console, and waited on, so the caller - Settings > Apps, or a shell - sees one
    # window and one exit code rather than a process that returns before it has done
    # anything.
    $process = Start-Process -FilePath $psExe -ArgumentList $arguments -NoNewWindow -Wait -PassThru

    # The staged copy cannot delete itself while it is the running script's home, so the
    # parent cleans it up now that the child has exited.
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue

    return $process.ExitCode
}

function Stop-RunningMarqora {
    $running = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)

    if ($running.Count -eq 0) {
        return
    }

    Write-Detail "asking $($running.Count) running instance(s) to close"

    # CloseMainWindow, not Stop-Process - the same request the window's close button sends,
    # so the app shuts down its own way and gets to ask about unsaved documents.
    #
    # This deliberately does not depend on -Quiet. Windows prefers QuietUninstallString
    # where it can, and letting that path hard-kill the app would mean an uninstall
    # started from Settings could discard unsaved work with nothing on screen to say so.
    foreach ($process in $running) {
        try {
            $process.CloseMainWindow() | Out-Null
        }
        catch {
            # Already gone, or no window to close.
        }
    }

    $deadline = (Get-Date).AddSeconds(15)

    while ((Get-Date) -lt $deadline -and @(Get-Process -Name $processName -ErrorAction SilentlyContinue).Count -gt 0) {
        Start-Sleep -Milliseconds 250
    }

    $remaining = @(Get-Process -Name $processName -ErrorAction SilentlyContinue)

    if ($remaining.Count -eq 0) {
        return
    }

    if (-not $Force) {
        throw "$appName is still running, and may be asking about unsaved changes. Finish closing it and try again, or pass -Force to close it regardless."
    }

    $remaining | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

function Remove-Shortcuts {
    param([string[]] $Paths)

    foreach ($path in $Paths) {
        if ($path -and (Test-Path -LiteralPath $path)) {
            Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
            Write-Detail (Split-Path -Leaf $path)
        }
    }
}

function Remove-Associations {
    $script = Join-Path $PSScriptRoot 'Register-FileAssociation.ps1'

    if (Test-Path -LiteralPath $script -PathType Leaf) {
        & $script -Unregister -Quiet
        return
    }

    # Deliberate duplication, and the only place in the tree it is tolerated. An
    # uninstaller that cannot find its helper must still finish: registry entries left
    # pointing at a deleted executable make Explorer offer a broken handler for every
    # markdown file, which is a worse outcome than repeating fifteen lines.
    Write-Warning 'Register-FileAssociation.ps1 was not found; removing the association keys directly.'

    $classesRoot = 'HKCU:\Software\Classes'

    foreach ($key in @((Join-Path $classesRoot $progId), (Join-Path $classesRoot "Applications\$exeName"))) {
        if (Test-Path -LiteralPath $key) {
            Remove-Item -LiteralPath $key -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    foreach ($extension in $defaultExtensions) {
        $openWith = Join-Path $classesRoot "$extension\OpenWithProgids"

        if (Test-Path -LiteralPath $openWith) {
            Remove-ItemProperty -LiteralPath $openWith -Name $progId -ErrorAction SilentlyContinue
        }
    }
}

function Update-ShellAssociations {
    $signature = @'
[DllImport("shell32.dll")]
public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
'@

    try {
        $shell = Add-Type -MemberDefinition $signature -Name 'MarqoraUninstallNotify' -Namespace 'Marqora' -PassThru
        $shell::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    catch {
        # Cosmetic: the keys are already gone, this only asks Explorer to notice sooner.
    }
}

function Remove-InstallDirectory {
    param([string] $Directory)

    if (-not (Test-Path -LiteralPath $Directory)) {
        Write-Detail 'already gone'
        return
    }

    # One retry: a virus scanner or an Explorer window that was looking at the folder a
    # moment ago can hold a handle for a second or two after the app has exited.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            Remove-Item -LiteralPath $Directory -Recurse -Force
            return
        }
        catch {
            if ($attempt -eq 3) {
                throw "Could not remove '$Directory'. Close anything using it and try again. ($($_.Exception.Message))"
            }

            Start-Sleep -Seconds 1
        }
    }
}

try {
    $target = Find-InstallDirectory -Candidate $InstallDir

    # Running from inside the directory about to be deleted: restart from the temp copy and
    # let that one do the work.
    if (-not $FromTemp -and $PSScriptRoot.StartsWith($target, [StringComparison]::OrdinalIgnoreCase)) {
        exit (Invoke-Relaunch -Directory $target)
    }

    $manifest = Read-Manifest -Directory $target
    $version = Get-ManifestValue -Manifest $manifest -Name 'version' -Default ''
    $dataDirectory = Get-ManifestValue -Manifest $manifest -Name 'dataDirectory' -Default $dataDirectory

    Write-Plain
    Write-Plain ("Uninstalling $appName $version").TrimEnd()
    Write-Plain

    if (-not (Test-Path -LiteralPath $target) -and -not (Test-Path -LiteralPath $arpKey)) {
        Write-Plain "$appName does not appear to be installed."
        exit 0
    }

    Write-Step '[1/4] Closing the app'
    Stop-RunningMarqora

    Write-Step '[2/4] Removing shortcuts and associations'

    # Default shortcut locations are removed alongside the recorded ones, so an entry the
    # manifest never captured - written by an older installer, say - does not survive.
    $recorded = @(Get-ManifestValue -Manifest $manifest -Name 'shortcuts' -Default @())
    $defaults = @(
        (Join-Path ([Environment]::GetFolderPath('Programs')) "$appName.lnk")
        (Join-Path ([Environment]::GetFolderPath('Desktop')) "$appName.lnk")
    )

    Remove-Shortcuts -Paths (@($recorded) + $defaults | Where-Object { $_ } | Select-Object -Unique)

    Remove-Associations
    Update-ShellAssociations
    Write-Detail 'markdown file types'

    Write-Step '[3/4] Removing the Settings entry'

    if (Test-Path -LiteralPath $arpKey) {
        Remove-Item -LiteralPath $arpKey -Recurse -Force
    }

    Write-Step '[4/4] Removing the app'
    Remove-InstallDirectory -Directory $target
    Write-Detail $target

    Write-Plain

    if ($RemoveUserData) {
        if (Test-Path -LiteralPath $dataDirectory) {
            Remove-Item -LiteralPath $dataDirectory -Recurse -Force
            Write-Plain "Removed your settings, snippets and logs from $dataDirectory"
        }
    }
    elseif (Test-Path -LiteralPath $dataDirectory) {
        Write-Plain 'Your settings, recent files and snippets were kept, in case this is an upgrade:'
        Write-Plain "  $dataDirectory"
        Write-Plain 'Delete that folder, or re-run this uninstaller with -RemoveUserData, to remove them too.'
    }

    Write-Plain
    Write-Plain "$appName has been uninstalled."
    Write-Plain

    if (-not $Quiet -and -not $NoPause -and [Environment]::UserInteractive) {
        Read-Host 'Press Enter to close'
    }

    exit 0
}
catch {
    # Write-Host rather than Write-Error on purpose. This script is read by someone who
    # clicked Uninstall in Settings, and a PowerShell error record - CategoryInfo,
    # FullyQualifiedErrorId, a caret pointing at the line that rethrew - buries the one
    # sentence that tells them what to do. The exit code still reports the failure.
    Write-Host ''
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ''

    if (-not $Quiet -and -not $NoPause -and [Environment]::UserInteractive) {
        Read-Host 'Press Enter to close' | Out-Null
    }

    exit 1
}
