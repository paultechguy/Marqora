#Requires -Version 5.1

<#
.SYNOPSIS
    Registers Marqora as a handler for markdown files.

.DESCRIPTION
    Writes the registry entries that let Windows offer Marqora for .md and its siblings,
    and that let Explorer hand several selected files to one process.

    Two things are registered:

    - A ProgId, PaulTechGuy.Marqora.Markdown, carrying the icon, the friendly type name and
      the open command. This is what a file extension points at once Marqora is the default.
    - An application entry under Applications\Marqora.exe, which is what Windows uses for the
      Open With list and for the association the user picks by hand.

    Both get MultiSelectModel = Player on the open verb. Without it Explorer starts one process
    per selected file; with it, selecting a dozen markdown files and pressing Enter passes them
    all on a single command line. Marqora is single-instance either way, so the files end up as
    tabs in one window regardless - this only decides whether Windows starts twelve processes
    to get there.

    Everything is written under HKCU. No elevation is needed, and nothing outside the current
    user is touched.

    Windows deliberately does not allow the default handler for an extension to be set
    programmatically: the choice is sealed with a per-user hash that only the Settings UI can
    produce. This script makes Marqora available and, where an association already points at
    it, corrects the command. Making it the default is a one-time manual step - right-click a
    .md file, Open with, Choose another app, Always.

.PARAMETER ExePath
    Full path to Marqora.exe. Defaults to the Debug build output in this repository, so the
    script can be run straight from a source tree with no arguments. An installer should pass
    the installed location explicitly.

.PARAMETER Extensions
    Extensions to register. Defaults to the set MarkdownFileTypes.Extensions accepts, minus
    .txt, which belongs to Notepad by convention and is better left alone.

.PARAMETER Unregister
    Removes everything this script creates instead of writing it.

.PARAMETER Quiet
    Suppresses progress output. Errors are still written and the exit code still reports them,
    which is the shape an installer wants.

.EXAMPLE
    pwsh .\build\Register-FileAssociation.ps1

    Registers the Debug build for the current user.

.EXAMPLE
    pwsh .\build\Register-FileAssociation.ps1 -ExePath 'C:\Program Files\Marqora\Marqora.exe' -Quiet

    The form an installer should use, run once per user after the files are in place.

.EXAMPLE
    pwsh .\build\Register-FileAssociation.ps1 -Unregister

    Removes the registration, for an uninstaller.

.NOTES
    Exit codes: 0 success, 1 failure.

    Windows PowerShell 5.1 compatible on purpose. New-Release.ps1 copies this script into
    the release zip, and the installer runs it on machines that have only the shell Windows
    ships with. Anything added here has to work under 5.1 as well as pwsh 7.
#>

[CmdletBinding()]
param(
    [string] $ExePath,

    [string[]] $Extensions = @('.md', '.markdown', '.mdown', '.mkd', '.mdx'),

    [switch] $Unregister,

    [switch] $Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# The ProgId is the identity Windows stores against an extension. Renaming it later would
# orphan every association already pointing at the old name, so it is fixed.
$progId = 'PaulTechGuy.Marqora.Markdown'
$friendlyTypeName = 'Markdown Document'
$appName = 'Marqora'

$classesRoot = 'HKCU:\Software\Classes'

function Write-Step {
    param([string] $Message)

    if (-not $Quiet) {
        Write-Host $Message
    }
}

function Resolve-MarqoraPath {
    param([string] $Candidate)

    if ($Candidate) {
        if (-not (Test-Path -LiteralPath $Candidate -PathType Leaf)) {
            throw "Marqora.exe was not found at '$Candidate'."
        }

        return (Resolve-Path -LiteralPath $Candidate).Path
    }

    # No path given: fall back to this repository's build output, so the script is useful
    # during development with no arguments at all.
    #
    # Release first, and never the newest by timestamp: an association is a durable, machine-
    # wide-feeling thing, and quietly repointing it at whichever configuration was built last
    # would leave Explorer opening a Debug build nobody meant to ship.
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $binRoot = Join-Path $repoRoot 'src\PaulTechGuy.MQ.App\bin'

    foreach ($configuration in @('Release', 'Debug')) {
        $found =
            Get-ChildItem -Path (Join-Path $binRoot $configuration) -Filter 'Marqora.exe' -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1

        if ($found) {
            if ($configuration -eq 'Debug') {
                Write-Warning 'No Release build found; registering the Debug build.'
            }

            return $found.FullName
        }
    }

    throw 'Marqora.exe was not found. Build the app first, or pass -ExePath.'
}

function Set-RegistryValue {
    param(
        [string] $Path,
        [string] $Name,
        [string] $Value
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -Path $Path -Force | Out-Null
    }

    # An empty name is the key's default value, which is where the shell looks for a command
    # string and for a ProgId's display name.
    New-ItemProperty -LiteralPath $Path -Name $Name -Value $Value -PropertyType String -Force | Out-Null
}

function Remove-RegistryKey {
    param([string] $Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Register-OpenVerb {
    param(
        [string] $KeyPath,
        [string] $Command
    )

    $shellOpen = Join-Path $KeyPath 'shell\open'

    # No display name on the verb: Explorer labels an unnamed open verb "Open", which is what
    # the menu should say. Naming it turns the first item of every markdown context menu into
    # "Open with Marqora", which reads like the submenu below it.

    # Player: one invocation receiving every selected item, up to 100 of them. The obvious
    # sounding Document is the opposite - it means the verb opens a top-level window per item,
    # so the shell starts one process each and caps the selection at 15.
    #
    # Without this the default for a plain command line is Document, which is exactly the
    # one-process-per-file behaviour being fixed here.
    Set-RegistryValue -Path $shellOpen -Name 'MultiSelectModel' -Value 'Player'

    Set-RegistryValue -Path (Join-Path $shellOpen 'command') -Name '(default)' -Value $Command
}

function Invoke-Registration {
    param([string] $Exe)

    # "%1" %* rather than either half alone. %1 is the item the shell is opening; %* is the
    # rest of the selection, which is what arrives when several files are activated at once.
    #
    # %* on its own is not the file: it expands to the parameters of the invocation, which for
    # a document activation are empty, and the app launches with nothing to open. %~, whose
    # documented meaning is the second item onwards, is not substituted at all here and reaches
    # the app as the literal string "%~". Both were measured, not assumed.
    $command = '"{0}" "%1" %*' -f $Exe
    $icon = '"{0}",0' -f $Exe

    Write-Step "Registering $Exe"

    # ---- the ProgId, which an extension points at once Marqora is the default
    $progIdPath = Join-Path $classesRoot $progId

    Set-RegistryValue -Path $progIdPath -Name '(default)' -Value $friendlyTypeName
    Set-RegistryValue -Path $progIdPath -Name 'FriendlyTypeName' -Value $friendlyTypeName
    Set-RegistryValue -Path (Join-Path $progIdPath 'DefaultIcon') -Name '(default)' -Value $icon

    Register-OpenVerb -KeyPath $progIdPath -Command $command

    # ---- the application entry, which drives the Open With list
    $appPath = Join-Path $classesRoot "Applications\$appName.exe"

    Set-RegistryValue -Path $appPath -Name 'FriendlyAppName' -Value $appName
    Set-RegistryValue -Path (Join-Path $appPath 'DefaultIcon') -Name '(default)' -Value $icon

    Register-OpenVerb -KeyPath $appPath -Command $command

    # SupportedTypes is what keeps Marqora out of the Open With list for file types it cannot
    # read, and in it for the ones it can.
    $supportedTypes = Join-Path $appPath 'SupportedTypes'

    foreach ($extension in $Extensions) {
        Set-RegistryValue -Path $supportedTypes -Name $extension -Value ''
    }

    # ---- offer Marqora for each extension without stealing any existing default
    foreach ($extension in $Extensions) {
        $extensionPath = Join-Path $classesRoot $extension

        Set-RegistryValue -Path (Join-Path $extensionPath 'OpenWithProgids') -Name $progId -Value ''
        Write-Step "  $extension"
    }

    # A path already registered by hand through Open With points at Applications\Marqora.exe
    # and has just been corrected. Anything else needs the user to choose Marqora once.
    Write-Step ''
    Write-Step 'Registered. If Windows does not already open these files with Marqora,'
    Write-Step 'right-click one, choose Open with, Choose another app, then Always.'
}

function Invoke-Unregistration {
    Write-Step 'Removing the Marqora file association'

    Remove-RegistryKey -Path (Join-Path $classesRoot $progId)
    Remove-RegistryKey -Path (Join-Path $classesRoot "Applications\$appName.exe")

    foreach ($extension in $Extensions) {
        $openWith = Join-Path $classesRoot "$extension\OpenWithProgids"

        if (Test-Path -LiteralPath $openWith) {
            Remove-ItemProperty -LiteralPath $openWith -Name $progId -ErrorAction SilentlyContinue
            Write-Step "  $extension"
        }
    }

    Write-Step ''
    Write-Step 'Removed. Windows may still list Marqora until the shell is restarted.'
}

# The shell caches associations aggressively; without this the change can take until the next
# sign-in to show up in Explorer.
function Update-ShellAssociations {
    $signature = @'
[DllImport("shell32.dll")]
public static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
'@

    try {
        $shell = Add-Type -MemberDefinition $signature -Name 'MarqoraShellNotify' -Namespace 'Marqora' -PassThru

        # SHCNE_ASSOCCHANGED with SHCNF_IDLIST.
        $shell::SHChangeNotify(0x08000000, 0x0000, [IntPtr]::Zero, [IntPtr]::Zero)
    }
    catch {
        # Cosmetic only: the registration itself has already succeeded.
        Write-Warning "Could not notify the shell of the change: $($_.Exception.Message)"
    }
}

try {
    if ($Unregister) {
        Invoke-Unregistration
    }
    else {
        Invoke-Registration -Exe (Resolve-MarqoraPath -Candidate $ExePath)
    }

    Update-ShellAssociations
    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
