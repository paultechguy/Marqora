<#
.SYNOPSIS
    Checks the app's buttons against docs/Button-App-Standards.md.

.DESCRIPTION
    A linter, not a formatter. Unlike Add-FileHeaders.ps1 there is nothing to write back:
    choosing a style for a button is a judgement about what the button means, and no script
    can make it. So both forms only report; -Check is the one that fails the build.

    What it enforces is the mechanical half of the standard - the half that is a fact about
    the text rather than a judgement about the design:

      keys        every Mq resource named in XAML or through MqStyles exists in App.xaml
      accent      nothing names AccentButtonStyle directly; it goes through the Mq styles
      geometry    no size, padding, radius or font set inline on a button
      dialogs     every ContentDialog names a DefaultButton
      destructive a destructive confirm does not leave Enter on the destructive answer
      web         webshell/diagram.css agrees with App.xaml about the compact metrics

    What it cannot do is judge design. It cannot tell a commit from a dismissal, so it cannot
    say which button in a row deserves the accent; it cannot know that "Clear" is destructive
    on a recent-file list and harmless on a search box; it cannot see a style assigned through
    a variable or a binding; and it cannot reason about Visibility, so it cannot know which
    buttons are on screen together. It measures nothing at all - it will not tell you a row
    grew wider or that a label clipped at 200% text scaling.

    The checklist at the end of docs/Button-App-Standards.md covers what is left. Read it
    rather than assuming a clean run means the buttons are right.

    An individual finding can be waived with a marker on the line above it:

        <!-- button-standard: exempt, mutually exclusive by Visibility -->
        // button-standard: exempt, the label is not destructive here

    Waivers are counted and listed, so they stay visible rather than becoming invisible debt.

.PARAMETER Check
    Exit non-zero when anything is found. This is the form to call from CI.

.PARAMETER Path
    Directories to scan, relative to the repo root. Defaults to the app project and webshell.

.EXAMPLE
    pwsh ./build/Test-ButtonStandards.ps1

.EXAMPLE
    pwsh ./build/Test-ButtonStandards.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch]$Check,
    [string[]]$Path = @('src/PaulTechGuy.MQ.App', 'webshell')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$appXaml = Join-Path $repoRoot 'src/PaulTechGuy.MQ.App/App.xaml'
$diagramCss = Join-Path $repoRoot 'webshell/diagram.css'

$findings = New-Object System.Collections.Generic.List[object]
$waived = New-Object System.Collections.Generic.List[object]

function Get-Relative {
    param([string]$FullName)
    return [System.IO.Path]::GetRelativePath($repoRoot, $FullName)
}

<#
    A finding, unless the line above it waives this rule.

    The marker sits on the previous line rather than the same one so it reads as a sentence
    about the line under it, and so it survives a reformat that wraps the attribute list.
#>
function Add-Finding {
    param(
        [string]$File,
        [string[]]$Lines,
        [int]$Index,
        [string]$Rule,
        [string]$Message
    )

    $record = [pscustomobject]@{
        File    = Get-Relative $File
        Line    = $Index + 1
        Rule    = $Rule
        Message = $Message
    }

    <#
        Walk back through the comment block above the line, not just the line above it.

        A waiver has to carry a reason, and a reason worth reading rarely fits on one line -
        the first one written for this ran to three. The middle lines of such a comment are
        plain prose and look like nothing in particular, so the walk stops on the things that
        are definitely not a comment instead: the start of another element, and the end of a
        statement. The marker itself is an explicit opt-in string, so crossing a few lines of
        prose cannot waive anything by accident.
    #>
    for ($back = $Index - 1; $back -ge 0 -and $back -ge $Index - 8; $back--) {
        $line = $Lines[$back].Trim()

        if ($line -match 'button-standard:\s*exempt') {
            $waived.Add($record)
            return
        }

        # Another element, or the end of a statement: past the top of any comment block.
        if ($line -match '^</?[A-Za-z]' -or $line -match '[;{}]\s*$') { break }
    }

    $findings.Add($record)
}

# ---------------------------------------------------------------- the files

$files = foreach ($relative in $Path) {
    # Relative to the repo root, which is how it is called; an absolute path is taken as given,
    # which is how it is exercised against a directory of deliberately bad files.
    $root = if ([System.IO.Path]::IsPathRooted($relative)) { $relative } else { Join-Path $repoRoot $relative }

    if (-not (Test-Path $root)) {
        Write-Warning "skipping $relative (not found)"
        continue
    }

    Get-ChildItem -Path $root -Recurse -File -Include '*.cs', '*.xaml', '*.css' |
        Where-Object { $_.FullName -notmatch '[\\/](obj|bin|vendor)[\\/]' } |
        Where-Object { $_.Name -notlike '*.g.cs' -and $_.Name -notlike '*.g.i.cs' }
}

$xamlFiles = @($files | Where-Object { $_.Extension -eq '.xaml' })
$csFiles = @($files | Where-Object { $_.Extension -eq '.cs' })

# ------------------------------------------------------- rule: keys resolve

<#
    Every Mq key that is used has to be defined.

    This is the rule MqStyles was written to make possible. A mistyped key in XAML throws at
    the moment the tree is inflated - which for a key inside a DataTemplate may be the first
    time some rarely-seen list has a row in it - and a mistyped key in code used to return
    null in silence and leave the control wearing the stock style.
#>
$defined = @{}
foreach ($file in @($appXaml) + @($xamlFiles | ForEach-Object { $_.FullName })) {
    if (-not (Test-Path $file)) { continue }
    foreach ($match in [regex]::Matches((Get-Content -Raw $file), 'x:Key="(Mq[A-Za-z0-9]*)"')) {
        $defined[$match.Groups[1].Value] = $true
    }
}

foreach ($file in $xamlFiles) {
    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($match in [regex]::Matches($lines[$i], '\{(?:Static|Theme)Resource\s+(Mq[A-Za-z0-9]*)\}')) {
            $key = $match.Groups[1].Value
            if (-not $defined.ContainsKey($key)) {
                Add-Finding $file.FullName $lines $i 'keys' "No resource named '$key' is defined."
            }
        }
    }
}

<#
    The C# side of the same rule.

    MqStyles is the only place code names a key, so its own members are read out of it and
    every other file is checked for members it does not have. That keeps the mapping in one
    place instead of restating the key strings here.
#>
$mqStylesPath = Join-Path $repoRoot 'src/PaulTechGuy.MQ.App/Views/MqStyles.cs'
if (Test-Path $mqStylesPath) {
    $mqStylesText = Get-Content -Raw $mqStylesPath

    $members = @{}
    foreach ($match in [regex]::Matches($mqStylesText, 'public static \w+ (\w+) =>')) {
        $members[$match.Groups[1].Value] = $true
    }

    foreach ($match in [regex]::Matches($mqStylesText, '(?:Style|Number)\("([^"]+)"\)')) {
        $key = $match.Groups[1].Value
        if (-not $defined.ContainsKey($key)) {
            $findings.Add([pscustomobject]@{
                File    = Get-Relative $mqStylesPath
                Line    = 0
                Rule    = 'keys'
                Message = "MqStyles asks for '$key', which App.xaml does not define."
            })
        }
    }

    foreach ($file in $csFiles) {
        if ($file.FullName -eq $mqStylesPath) { continue }

        $lines = Get-Content $file.FullName
        for ($i = 0; $i -lt $lines.Count; $i++) {
            foreach ($match in [regex]::Matches($lines[$i], 'MqStyles\.(\w+)')) {
                $member = $match.Groups[1].Value
                if ($member -ne 'Verify' -and -not $members.ContainsKey($member)) {
                    Add-Finding $file.FullName $lines $i 'keys' "MqStyles has no member '$member'."
                }
            }
        }
    }
}

# ------------------------------------------------------ rule: accent by name

<#
    The accent is reached through the Mq styles, never named directly.

    Not pedantry: MqPrimaryCommandButtonStyle restates padding, font and border because
    AccentButtonStyle declares no BasedOn and sets six properties. A button that names
    AccentButtonStyle itself is a different size from the neutral one beside it.

    App.xaml is where the two Mq styles legitimately derive from it, so it is exempt.
#>
foreach ($file in @($xamlFiles) + @($csFiles)) {
    if ($file.FullName -eq $appXaml) { continue }

    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'AccentButtonStyle') {
            Add-Finding $file.FullName $lines $i 'accent' 'Names AccentButtonStyle directly; use MqPrimaryCommandButtonStyle.'
        }
    }
}

# --------------------------------------------------------- rule: geometry

<#
    No size, padding, radius or font set on a button in passing.

    XAML first. The start tag is matched across lines because the attributes in this project
    are one per line, and the closing bracket is found by scanning rather than by a lazy
    character class, so an attribute value containing '>' cannot end the tag early.
#>
$buttonTag = '^\s*<(Button|ToggleButton|DropDownButton|HyperlinkButton)\b'
$geometry = 'Padding|CornerRadius|FontSize|FontFamily|Background|Foreground|MinWidth|MinHeight|\bWidth|\bHeight'

foreach ($file in $xamlFiles) {
    if ($file.FullName -eq $appXaml) { continue }

    $lines = Get-Content $file.FullName

    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch $buttonTag) { continue }

        # Collect the whole start tag.
        $tag = ''
        $end = $i
        while ($end -lt $lines.Count) {
            $tag += $lines[$end] + "`n"
            if ($lines[$end] -match '>') { break }
            $end++
        }

        if ($tag -match "\s($geometry)\s*=") {
            $property = $Matches[1].Trim()
            Add-Finding $file.FullName $lines $i 'geometry' "Sets $property inline; put it in a shared style."
        }

        $i = $end
    }
}

<#
    The same in code. Both an object initialiser and a later assignment, because a code-built
    view does both - FindAllWindow set Height and FontSize on separate lines after
    construction, which an initialiser-only pattern would have missed entirely.
#>
$declared = 'new (?:Button|ToggleButton|DropDownButton|HandCursorButton)'

foreach ($file in $csFiles) {
    $lines = Get-Content $file.FullName

    # Locals declared as buttons, so a later assignment can be attributed to one.
    $buttons = @{}
    foreach ($line in $lines) {
        if ($line -match '(?:var|Button|ToggleButton|DropDownButton)\s+(\w+)\s*=\s*' + $declared) {
            $buttons[$Matches[1]] = $true
        }
        elseif ($line -match 'private readonly \w+ (_\w+)\s*=\s*' + $declared) {
            $buttons[$Matches[1]] = $true
        }
    }

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]

        if ($line -match $declared) {
            # Object initialiser on this line or the ones that follow, to the closing brace.
            $block = ''
            $end = $i
            $depth = 0
            while ($end -lt $lines.Count -and $end -lt $i + 12) {
                $block += $lines[$end] + "`n"
                $depth += ([regex]::Matches($lines[$end], '\{')).Count
                $depth -= ([regex]::Matches($lines[$end], '\}')).Count
                if ($depth -le 0 -and $end -gt $i) { break }
                if ($lines[$end] -match '\};?\s*$') { break }
                $end++
            }

            if ($block -match "[\{,]\s*($geometry)\s*=") {
                Add-Finding $file.FullName $lines $i 'geometry' "Sets $($Matches[1].Trim()) on a new button; use a shared style."
            }
        }

        # A later assignment onto something known to be a button.
        if ($line -match '^\s*(\w+)\.(' + $geometry + ')\s*=' -and $buttons.ContainsKey($Matches[1])) {
            Add-Finding $file.FullName $lines $i 'geometry' "Sets $($Matches[2].Trim()) on '$($Matches[1])'; use a shared style."
        }
    }
}

# ---------------------------------------------------------- rule: dialogs

<#
    Every ContentDialog says which button Enter reaches.

    Left unset, WinUI reaches none of them, and the dialog answers Enter by doing nothing -
    which reads as a stuck dialog rather than as a deliberate choice.
#>
foreach ($file in $csFiles) {
    $text = Get-Content -Raw $file.FullName

    # A class declaration, not a generic constraint: "where T : ContentDialog" in
    # DialogExtensions is the helper every dialog goes through, not a dialog itself.
    if ($text -match 'class\s+\w+\s*:\s*ContentDialog\b' -and $text -notmatch 'DefaultButton\s*=') {
        $findings.Add([pscustomobject]@{
            File    = Get-Relative $file.FullName
            Line    = 0
            Rule    = 'dialogs'
            Message = 'A ContentDialog with no DefaultButton; Enter would do nothing.'
        })
    }
}

# ------------------------------------------------------ rule: destructive

<#
    A confirm whose primary throws work away must not leave Enter on it.

    The word list is a prompt to think, not a verdict - "Clear" is destructive on a recent
    file list and harmless on a search box - so a false positive here is waived on the line
    rather than argued with. It is the rule that would have caught the reload-from-disk
    prompt, whose own comment claimed Cancel was the default while the code said otherwise.
#>
$destructive = 'Discard|Delete|Remove|Clear|Reset|Restore|Overwrite|Erase|Don.t save'

foreach ($file in $csFiles) {
    $lines = Get-Content $file.FullName
    $text = $lines -join "`n"

    foreach ($match in [regex]::Matches($text, 'ConfirmAsync\((?:[^;]*?)\)\s*\.ConfigureAwait')) {
        $call = $match.Value

        <#
            The secondary answer is dropped before the call is read.

            "Save changes?" offers Save, Discard and Cancel: its primary is Save, which is the
            safe answer, and the destructive word is on the alternative. Reading the whole call
            flagged it, which is the wrong answer - the rule is about which button Enter lands
            on, and Enter lands on the primary.
        #>
        $call = [regex]::Replace($call, 'secondaryText:\s*\$?"[^"]*"', '')

        if ($call -notmatch $destructive) { continue }

        if ($call -match 'destructivePrimary:\s*true') { continue }

        $index = ($text.Substring(0, $match.Index) -split "`n").Count - 1
        Add-Finding $file.FullName $lines $index 'destructive' `
            'A destructive-sounding primary without destructivePrimary: true; Enter would destroy.'
    }
}

# ------------------------------------------------------------- rule: web

<#
    webshell/diagram.css and App.xaml have to agree.

    The one duplication the standard allows: five zoom buttons do not justify pushing metrics
    across the WebView bridge the way MatchColors pushes colours, but they do justify a test.
    This is that test, and it is the only thing keeping the copy honest.
#>
if ((Test-Path $appXaml) -and (Test-Path $diagramCss)) {
    $xamlText = Get-Content -Raw $appXaml
    $cssText = Get-Content -Raw $diagramCss

    function Get-XamlDouble {
        param([string]$Key)
        if ($xamlText -match "<x:Double x:Key=`"$Key`">([\d.]+)</x:Double>") { return $Matches[1] }
        return $null
    }

    function Get-CssPixels {
        param([string]$Name)
        if ($cssText -match "--$Name\s*:\s*(\d+)px") { return $Matches[1] }
        return $null
    }

    $pairs = @(
        @{ Xaml = 'MqCompactButtonHeight'; Css = 'mq-btn-height'; What = 'compact button height' }
    )

    foreach ($pair in $pairs) {
        $left = Get-XamlDouble $pair.Xaml
        $right = Get-CssPixels $pair.Css

        if ($null -eq $left -or $null -eq $right) {
            $findings.Add([pscustomobject]@{
                File    = 'webshell/diagram.css'
                Line    = 0
                Rule    = 'web'
                Message = "Could not read the $($pair.What) from both App.xaml and diagram.css."
            })
            continue
        }

        if ([double]$left -ne [double]$right) {
            $findings.Add([pscustomobject]@{
                File    = 'webshell/diagram.css'
                Line    = 0
                Rule    = 'web'
                Message = "The $($pair.What) is $left in App.xaml and $right in diagram.css."
            })
        }
    }

    # The radius is a CornerRadius rather than a Double, so it is read on its own terms.
    if ($xamlText -match '<CornerRadius x:Key="MqPillRadius">(\d+)</CornerRadius>') {
        $pill = $Matches[1]
        $cssRadius = Get-CssPixels 'mq-btn-radius'

        if ($null -ne $cssRadius -and [double]$pill -ne [double]$cssRadius) {
            $findings.Add([pscustomobject]@{
                File    = 'webshell/diagram.css'
                Line    = 0
                Rule    = 'web'
                Message = "The button radius is $pill in App.xaml (MqPillRadius) and $cssRadius in diagram.css."
            })
        }
    }
}

# ---------------------------------------------------------------- report

$scanned = @($files).Count

if ($findings.Count -gt 0) {
    Write-Host "Button standard: $($findings.Count) finding(s) in $scanned file(s)." -ForegroundColor Red

    foreach ($group in $findings | Group-Object Rule | Sort-Object Name) {
        Write-Host ""
        Write-Host "  [$($group.Name)]" -ForegroundColor Yellow

        foreach ($finding in $group.Group | Sort-Object File, Line) {
            $where = if ($finding.Line -gt 0) { "$($finding.File):$($finding.Line)" } else { $finding.File }
            Write-Host "    $where" -ForegroundColor DarkGray
            Write-Host "      $($finding.Message)"
        }
    }

    Write-Host ""
    Write-Host "See docs/Button-App-Standards.md. A line can be waived with" -ForegroundColor DarkGray
    Write-Host "'button-standard: exempt, <reason>' in a comment above it." -ForegroundColor DarkGray
}
else {
    Write-Host "Button standard: nothing to report across $scanned file(s)." -ForegroundColor Green
}

if ($waived.Count -gt 0) {
    Write-Host ""
    Write-Host "Waived by an exempt marker: $($waived.Count)" -ForegroundColor DarkGray
    foreach ($item in $waived | Sort-Object File, Line) {
        Write-Host "  $($item.File):$($item.Line)  [$($item.Rule)]" -ForegroundColor DarkGray
    }
}

Write-Host ""
Write-Host "This covers the mechanical half only. It cannot tell which button commits," -ForegroundColor DarkGray
Write-Host "nor measure a row - the checklist in the document covers the rest." -ForegroundColor DarkGray

if ($Check -and $findings.Count -gt 0) {
    exit 1
}

exit 0
