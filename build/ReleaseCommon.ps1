#Requires -Version 7.0

<#
.SYNOPSIS
    Helpers shared by New-Release.ps1, New-ReleaseNotes.ps1 and Publish-Release.ps1.

.DESCRIPTION
    Dot-source it. It defines functions and does nothing on its own:

        . (Join-Path $PSScriptRoot 'ReleaseCommon.ps1')

    Three scripts print the same kind of progress, run the same external tools and check the
    same facts about the repository. Keeping one copy here means a fix to the git handling or
    the step formatting lands everywhere at once, rather than in whichever script someone
    happened to be looking at.

    Nothing here writes to the network or to the repository except Set-RepoVersion. The
    pre-flight gates are all read-only, which is what makes it safe to run them before
    deciding whether a release should happen at all.

.NOTES
    Callers are expected to have set 'Set-StrictMode -Version Latest' and
    '$ErrorActionPreference = Stop'. Functions throw on failure rather than returning a code,
    so a caller's try/catch is what turns a broken gate into a tidy exit.
#>

# ---------------------------------------------------------------------------------------------
#  Progress output
#
#  Two shapes. Numbered tasks - "[2/5] tag ... ok" - for steps that change something, so the
#  reader can see how much is left. Unnumbered checks - "  tag and release ... free" - for
#  pre-flight, where the count is uninteresting and the answer is what matters.
# ---------------------------------------------------------------------------------------------

# Both shapes pad to the same column so a pre-flight block and the numbered steps under it
# line up as one list. 36 is what New-Release.ps1 has always used.
$MqLineWidth = 36

function Initialize-TaskList {
    param([Parameter(Mandatory)][int] $Total)

    $script:MqTaskTotal = $Total
    $script:MqTaskStep = 0
}

function Write-Task {
    param([Parameter(Mandatory)][string] $Label)

    $script:MqTaskStep++
    $text = '  [{0}/{1}] {2} ' -f $script:MqTaskStep, $script:MqTaskTotal, $Label
    Write-Host $text.PadRight($MqLineWidth, '.') -NoNewline
}

function Write-Check {
    param([Parameter(Mandatory)][string] $Label)

    Write-Host ('    {0} ' -f $Label).PadRight($MqLineWidth, '.') -NoNewline
}

function Write-Done {
    param([string] $Note)

    if ($Note) {
        Write-Host ' ok' -ForegroundColor Green -NoNewline
        Write-Host " ($Note)" -ForegroundColor DarkGray
    }
    else {
        Write-Host ' ok' -ForegroundColor Green
    }
}

function Write-Failed {
    param([string] $Note)

    if ($Note) {
        Write-Host ' failed' -ForegroundColor Red -NoNewline
        Write-Host " ($Note)" -ForegroundColor DarkGray
    }
    else {
        Write-Host ' failed' -ForegroundColor Red
    }
}

function Write-Skipped {
    param([string] $Note)

    Write-Host ' skipped' -ForegroundColor DarkGray -NoNewline
    Write-Host " ($Note)" -ForegroundColor DarkGray
}

# The message a script dies with. Write-Error would frame this with a caret pointing at the
# catch block, which says where the throw was rethrown rather than what the reader did wrong,
# and it mangles the multi-line explanations the gates use. Exit codes carry the failure for
# anything reading this from a script.
function Write-Failure {
    param([Parameter(Mandatory)][string] $Message)

    Write-Host ''

    foreach ($line in ($Message -split '\r?\n')) {
        Write-Host "  $line" -ForegroundColor Red
    }

    Write-Host ''
}

# An indented aside under a step: something worth saying that is not worth failing over.
function Write-Note {
    param([Parameter(Mandatory)][string] $Message)

    Write-Host "        $Message" -ForegroundColor Yellow
}

function Write-Hint {
    param([Parameter(Mandatory)][string] $Message)

    Write-Host "        $Message" -ForegroundColor DarkGray
}

# ---------------------------------------------------------------------------------------------
#  Sizes
# ---------------------------------------------------------------------------------------------

function Format-Size {
    param([Parameter(Mandatory)][long] $Bytes)

    if ($Bytes -ge 1GB) {
        return '{0:N1} GB' -f ($Bytes / 1GB)
    }

    return '{0:N0} MB' -f ($Bytes / 1MB)
}

function Get-DirectorySize {
    param([Parameter(Mandatory)][string] $Path)

    $measured = Get-ChildItem -LiteralPath $Path -Recurse -File -Force | Measure-Object -Property Length -Sum

    if ($measured.Sum) {
        return [long] $measured.Sum
    }

    return [long] 0
}

# ---------------------------------------------------------------------------------------------
#  External tools
#
#  All three capture rather than stream, so the step lines stay one line each. The whole log
#  is printed when something fails, which is the only time anyone wants to read it.
# ---------------------------------------------------------------------------------------------

function Write-CapturedOutput {
    param($Output)

    if (-not $Output) {
        return
    }

    Write-Host ''
    $Output | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    Write-Host ''
}

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $FailureMessage,
        [switch] $Stream
    )

    if ($Stream) {
        Write-Host ''
        & dotnet @Arguments
        $streamed = $LASTEXITCODE

        if ($streamed -ne 0) {
            throw "$FailureMessage (dotnet exit code $streamed)"
        }

        return
    }

    $output = & dotnet @Arguments 2>&1
    $code = $LASTEXITCODE

    if ($code -ne 0) {
        Write-Failed
        Write-CapturedOutput $output
        throw "$FailureMessage (dotnet exit code $code)"
    }
}

function Invoke-Git {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [string] $FailureMessage,
        [switch] $AllowFailure
    )

    $output = & git @Arguments 2>&1
    $code = $LASTEXITCODE

    if ($code -ne 0 -and -not $AllowFailure) {
        Write-Failed
        Write-CapturedOutput $output
        throw "$FailureMessage (git exit code $code)"
    }

    return [pscustomobject]@{
        ExitCode = $code
        Output   = if ($output) { ($output | Out-String).TrimEnd() } else { '' }
    }
}

function Invoke-Gh {
    param(
        [Parameter(Mandatory)][string[]] $Arguments,
        [string] $FailureMessage,
        [switch] $AllowFailure
    )

    $output = & gh @Arguments 2>&1
    $code = $LASTEXITCODE

    if ($code -ne 0 -and -not $AllowFailure) {
        Write-Failed
        Write-CapturedOutput $output
        throw "$FailureMessage (gh exit code $code)"
    }

    return [pscustomobject]@{
        ExitCode = $code
        Output   = if ($output) { ($output | Out-String).TrimEnd() } else { '' }
    }
}

# ---------------------------------------------------------------------------------------------
#  Versions
# ---------------------------------------------------------------------------------------------

# Matches the single <Version> element in Directory.Build.props and not <LangVersion> above it,
# because the '<' in the pattern anchors to the start of the tag name.
$MqVersionPattern = '(?m)^(\s*)<Version>([^<]*)</Version>'

function Test-VersionString {
    param([Parameter(Mandatory)][string] $Version)

    return $Version -match '^\d+\.\d+\.\d+$'
}

function Assert-VersionString {
    param([Parameter(Mandatory)][string] $Version)

    if (-not (Test-VersionString $Version)) {
        throw "'$Version' is not a version. Expected three numbers, as in 0.3.0."
    }
}

function Get-RepoVersion {
    param([Parameter(Mandatory)][string] $PropsPath)

    $text = [System.IO.File]::ReadAllText($PropsPath)
    $match = [regex]::Match($text, $MqVersionPattern)

    if (-not $match.Success) {
        throw "No <Version> element in '$PropsPath'."
    }

    return $match.Groups[2].Value.Trim()
}

function Set-RepoVersion {
    param(
        [Parameter(Mandatory)][string] $PropsPath,
        [Parameter(Mandatory)][string] $Version
    )

    Assert-VersionString $Version

    $text = [System.IO.File]::ReadAllText($PropsPath)

    if (-not [regex]::IsMatch($text, $MqVersionPattern)) {
        throw "No <Version> element in '$PropsPath'."
    }

    # ${1} keeps the file's own indentation rather than imposing four spaces.
    $replacement = '${1}<Version>' + $Version + '</Version>'
    $updated = [regex]::Replace($text, $MqVersionPattern, $replacement)

    if ($updated -ceq $text) {
        return $false
    }

    # No BOM, and ReadAllText/WriteAllText round-trips the file's existing line endings.
    [System.IO.File]::WriteAllText($PropsPath, $updated, [System.Text.UTF8Encoding]::new($false))

    return $true
}

# The newest tag by version order, or $null when nothing is tagged yet. Tags that are not
# v<major>.<minor>.<patch> are ignored rather than guessed at.
function Get-LatestVersionTag {
    param([Parameter(Mandatory)][string] $RepoRoot)

    $tags = (Invoke-Git -Arguments @('-C', $RepoRoot, 'tag', '--list', 'v*')).Output

    if (-not $tags) {
        return $null
    }

    $parsed = $tags -split '\r?\n' |
        Where-Object { $_ -match '^v(\d+\.\d+\.\d+)$' } |
        ForEach-Object { [pscustomobject]@{ Tag = $_; Version = [version] $Matches[1] } } |
        Sort-Object -Property Version

    if (-not $parsed) {
        return $null
    }

    return @($parsed)[-1]
}

# ---------------------------------------------------------------------------------------------
#  Templates
#
#  {{TOKEN}} substitution, the same idiom build\installer\README.txt already uses for its
#  version. A plain string replace; there is no templating engine here and no need for one.
# ---------------------------------------------------------------------------------------------

function Expand-Template {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][hashtable] $Token,
        [switch] $AllowUnresolved
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Template not found: '$Path'."
    }

    $text = [System.IO.File]::ReadAllText($Path)

    foreach ($key in $Token.Keys) {
        $text = $text.Replace('{{' + $key + '}}', [string] $Token[$key])
    }

    if (-not $AllowUnresolved) {
        $left = [regex]::Matches($text, '\{\{[A-Z0-9_]+\}\}') | ForEach-Object { $_.Value } | Sort-Object -Unique

        if ($left) {
            throw "Template '$([System.IO.Path]::GetFileName($Path))' has tokens nothing filled in: $($left -join ', ')"
        }
    }

    return $text
}

function Save-Text {
    param(
        [Parameter(Mandatory)][string] $Path,
        [Parameter(Mandatory)][string] $Text
    )

    $directory = Split-Path -Parent $Path

    if ($directory -and -not (Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    [System.IO.File]::WriteAllText($Path, $Text, [System.Text.UTF8Encoding]::new($false))
}

# ---------------------------------------------------------------------------------------------
#  Pre-flight gates
#
#  Every one is read-only. Each prints its own line and throws on the first failure, so the
#  output reads as a checklist that stops where the problem is. The caller picks which gates
#  apply: scaffolding release notes has no business running the test suite.
# ---------------------------------------------------------------------------------------------

function Test-GateTooling {
    Write-Check 'tooling'

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Failed
        throw "The GitHub CLI (gh) is not on PATH. Install it from https://cli.github.com and run 'gh auth login'."
    }

    $status = Invoke-Gh -Arguments @('auth', 'status') -AllowFailure

    if ($status.ExitCode -ne 0) {
        Write-Failed
        Write-CapturedOutput $status.Output
        throw "gh is installed but not signed in. Run 'gh auth login'."
    }

    $account = if ($status.Output -match 'account (\S+)') { $Matches[1] } else { 'signed in' }
    Write-Done $account
}

function Test-GateTree {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Branch,
        [string[]] $AllowDirty = @()
    )

    Write-Check 'branch and tree'

    # Without this the sync check below compares against a stale remote ref and passes when it
    # should not. It is the only gate that touches the network. --tags matters as well: the
    # tag gate reads local tags to work out which version came last, and a local tag list that
    # is missing what origin already has would let a released version look free.
    Invoke-Git -Arguments @('-C', $RepoRoot, 'fetch', 'origin', '--tags', '--quiet') -FailureMessage 'Could not reach origin.' | Out-Null

    $current = (Invoke-Git -Arguments @('-C', $RepoRoot, 'rev-parse', '--abbrev-ref', 'HEAD')).Output

    if ($current -ne $Branch) {
        Write-Failed
        throw "A release runs from '$Branch'; HEAD is on '$current'. Land your work on '$Branch' first."
    }

    # --untracked-files=all matters: without it git collapses a wholly untracked directory to
    # 'docs/', which never matches the specific file an AllowDirty caller is expecting.
    $dirty = (Invoke-Git -Arguments @('-C', $RepoRoot, 'status', '--porcelain', '--untracked-files=all')).Output

    if ($dirty) {
        $unexpected = $dirty -split '\r?\n' | Where-Object {
            $path = $_.Substring(3).Trim('"')
            $path -notin $AllowDirty
        }

        if ($unexpected) {
            Write-Failed
            Write-CapturedOutput $unexpected
            $allowed = if ($AllowDirty) { " Only $($AllowDirty -join ' and ') may be uncommitted here." } else { '' }
            throw "The working tree has changes that are not part of this release.$allowed"
        }
    }

    $local = (Invoke-Git -Arguments @('-C', $RepoRoot, 'rev-parse', $Branch)).Output
    $remote = (Invoke-Git -Arguments @('-C', $RepoRoot, 'rev-parse', "origin/$Branch")).Output

    if ($local -ne $remote) {
        Write-Failed
        $ahead = (Invoke-Git -Arguments @('-C', $RepoRoot, 'rev-list', '--count', "origin/$Branch..$Branch")).Output
        $behind = (Invoke-Git -Arguments @('-C', $RepoRoot, 'rev-list', '--count', "$Branch..origin/$Branch")).Output
        throw "'$Branch' and 'origin/$Branch' disagree: $ahead commit(s) ahead, $behind behind. Push or pull before releasing."
    }

    Write-Done "$Branch, clean, in sync"
}

function Test-GateAncestor {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [string] $Released = 'master',
        [string] $Development = 'dev'
    )

    Write-Check "$Released ancestor of $Development"

    $result = Invoke-Git `
        -Arguments @('-C', $RepoRoot, 'merge-base', '--is-ancestor', "origin/$Released", "origin/$Development") `
        -AllowFailure

    if ($result.ExitCode -ne 0) {
        Write-Failed
        throw @"
'origin/$Released' is not an ancestor of 'origin/$Development', so the fast-forward would fail.

Something has been committed to '$Released' that is not on '$Development'. The repository ruleset
blocks force-pushing and cannot be bypassed, so the fix is to merge '$Released' into
'$Development' first and release from the result.
"@
    }

    $behind = (Invoke-Git -Arguments @('-C', $RepoRoot, 'rev-list', '--count', "origin/$Released..origin/$Development")).Output
    Write-Done "$behind commit(s) to promote"
}

function Test-GateTagFree {
    param(
        [Parameter(Mandatory)][string] $RepoRoot,
        [Parameter(Mandatory)][string] $Version
    )

    Write-Check 'tag and release'

    $tag = "v$Version"

    $local = (Invoke-Git -Arguments @('-C', $RepoRoot, 'tag', '--list', $tag)).Output

    if ($local) {
        Write-Failed
        throw "The tag '$tag' already exists locally. Delete it with 'git tag -d $tag' or pick another version."
    }

    $remote = (Invoke-Git -Arguments @('-C', $RepoRoot, 'ls-remote', '--tags', 'origin', "refs/tags/$tag")).Output

    if ($remote) {
        Write-Failed
        throw "The tag '$tag' is already on origin. Version $Version has been released; pick a newer one."
    }

    # Only meaningful when gh is available; New-ReleaseNotes.ps1 deliberately does not require it.
    if (Get-Command gh -ErrorAction SilentlyContinue) {
        $release = Invoke-Gh -Arguments @('release', 'view', $tag, '--repo', (Get-RepoSlug -RepoRoot $RepoRoot)) -AllowFailure

        if ($release.ExitCode -eq 0) {
            Write-Failed
            throw "A GitHub release for '$tag' already exists. Delete it with 'gh release delete $tag' or pick another version."
        }
    }

    $latest = Get-LatestVersionTag -RepoRoot $RepoRoot

    if ($latest -and [version] $Version -le $latest.Version) {
        Write-Failed
        throw "Version $Version does not come after the newest tag, $($latest.Tag). Releases only move forward."
    }

    $note = if ($latest) { "$tag is free, after $($latest.Tag)" } else { "$tag is free, first release" }
    Write-Done $note
}

function Test-GateVersionMatch {
    param(
        [Parameter(Mandatory)][string] $PropsPath,
        [Parameter(Mandatory)][string] $Version
    )

    Write-Check 'version'

    $actual = Get-RepoVersion -PropsPath $PropsPath

    if ($actual -ne $Version) {
        Write-Failed
        throw @"
Directory.Build.props says $actual but you asked to release $Version.

Either the version bump has not been committed and pushed to dev yet, or the wrong number was
passed. Run 'pwsh .\build\New-ReleaseNotes.ps1 -Version $Version' if the bump is still to do.
"@
    }

    Write-Done $actual
}

function Test-GateNotesAbsent {
    param(
        [Parameter(Mandatory)][string] $NotesPath,
        [switch] $Force
    )

    Write-Check 'release notes'

    if ((Test-Path -LiteralPath $NotesPath -PathType Leaf) -and -not $Force) {
        Write-Failed
        $relative = Split-Path -Leaf $NotesPath
        throw "'$relative' already exists. Edit it, or pass -Force to start again from the template."
    }

    Write-Done 'ready to scaffold'
}

# What a notes file actually says, with the scaffolding stripped out: no HTML comments, no
# placeholder lines, no blank runs. Comparing two files this way asks whether they differ in
# substance rather than in whitespace.
function Get-NotesSubstance {
    param([Parameter(Mandatory)][AllowEmptyString()][string] $Text)

    $withoutComments = [regex]::Replace($Text, '(?s)<!--.*?-->', '')

    $lines = $withoutComments -split '\r?\n' |
        Where-Object { $_ -notmatch 'TODO' } |
        ForEach-Object { $_.TrimEnd() } |
        Where-Object { $_ }

    return ($lines -join "`n").Trim()
}

function Test-GateNotesReady {
    param(
        [Parameter(Mandatory)][string] $NotesPath,
        [Parameter(Mandatory)][string] $TemplatePath,
        [Parameter(Mandatory)][string] $Version
    )

    Write-Check 'release notes'

    if (-not (Test-Path -LiteralPath $NotesPath -PathType Leaf)) {
        Write-Failed
        throw @"
No release notes at '$NotesPath'.

Run 'pwsh .\build\New-ReleaseNotes.ps1 -Version $Version', fill them in, and commit them to dev.
"@
    }

    $text = [System.IO.File]::ReadAllText($NotesPath)
    $lines = $text -split '\r?\n'
    $todos = for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'TODO') {
            '{0,6}: {1}' -f ($i + 1), $lines[$i].Trim()
        }
    }

    if ($todos) {
        Write-Failed
        Write-CapturedOutput $todos
        throw 'The release notes still carry template placeholders. Fill them in and commit before releasing.'
    }

    # The placeholder check above already catches a scaffold nobody opened. This catches the
    # other half of the same mistake: placeholders deleted, nothing written in their place.
    # Both sides are reduced to their real content first - no guidance comment, no placeholder
    # lines, no blank runs - so that deleting the comment does not read as having written
    # something.
    $pristine = Expand-Template -Path $TemplatePath -Token @{ VERSION = $Version }

    if ((Get-NotesSubstance $text) -ceq (Get-NotesSubstance $pristine)) {
        Write-Failed
        throw 'The release notes say nothing the template did not. Write them before releasing.'
    }

    $words = ($text -split '\s+' | Where-Object { $_ }).Count
    Write-Done "$words words"
}

function Test-GateTests {
    param(
        [Parameter(Mandatory)][string] $Solution,
        [Parameter(Mandatory)][string] $Configuration,
        [switch] $Stream
    )

    Write-Check 'tests'
    Invoke-Dotnet `
        -Arguments @('test', $Solution, '-c', $Configuration, '--nologo') `
        -FailureMessage 'Tests failed.' `
        -Stream:$Stream
    Write-Done 'passed'
}

function Test-GateHeaders {
    param([Parameter(Mandatory)][string] $ScriptPath)

    Write-Check 'licence headers'

    $output = & pwsh -NoProfile -File $ScriptPath -Check 2>&1
    $code = $LASTEXITCODE

    if ($code -ne 0) {
        Write-Failed
        Write-CapturedOutput $output
        throw 'Some files are missing the licence header. Run: pwsh .\build\Add-FileHeaders.ps1'
    }

    Write-Done
}

function Test-GateBuild {
    param(
        [Parameter(Mandatory)][string] $Solution,
        [Parameter(Mandatory)][string] $Configuration,
        [switch] $Stream
    )

    Write-Check 'build, no warnings'
    Invoke-Dotnet `
        -Arguments @('build', $Solution, '-c', $Configuration, '-warnaserror', '--nologo') `
        -FailureMessage 'The build produced errors or warnings.' `
        -Stream:$Stream
    Write-Done 'clean'
}

# ---------------------------------------------------------------------------------------------
#  Repository identity
# ---------------------------------------------------------------------------------------------

# owner/name, read from the origin remote rather than hard-coded, so a fork behaves sensibly.
function Get-RepoSlug {
    param([Parameter(Mandatory)][string] $RepoRoot)

    $url = (Invoke-Git -Arguments @('-C', $RepoRoot, 'remote', 'get-url', 'origin')).Output

    if ($url -match '[:/]([^/:]+)/([^/]+?)(\.git)?$') {
        return "$($Matches[1])/$($Matches[2])"
    }

    throw "Could not work out the GitHub repository from the origin remote: '$url'"
}
