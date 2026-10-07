<#
.SYNOPSIS
    Rewrites the tables in docs/Paper-Design.md from the paper spec.

.DESCRIPTION
    Type and space on paper are written down once, in src/PaulTechGuy.MQ.Domain/PaperSpec.cs and
    PaperFaces.cs. The design document shows them as tables for the person reading it, and the
    tables are generated, so a value changed in the code reaches the document without anyone
    remembering to copy it.

    The generating is done by PaperSpecTests in the Word export's tests, which read the spec as
    the compiler sees it - a script parsing the C# would be a second copy of it by another name.
    This runs that test: with the write switch set, it replaces the marked block in the
    document; with -Check, it only reports.

    The test runs in every test pass, so a document that has fallen behind fails there as well;
    this is the way to bring it up to date.

.PARAMETER Check
    Report only, and exit non-zero if the document has fallen behind the spec.

.EXAMPLE
    pwsh ./build/Update-PaperDesign.ps1
    pwsh ./build/Update-PaperDesign.ps1 -Check
#>
[CmdletBinding()]
param(
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo 'tests/PaulTechGuy.MQ.Docx.Tests/PaulTechGuy.MQ.Docx.Tests.csproj'

if ($Check) {
    Remove-Item Env:MARQORA_WRITE_PAPER_DOCS -ErrorAction SilentlyContinue
} else {
    $env:MARQORA_WRITE_PAPER_DOCS = '1'
}

try {
    dotnet test $project --filter 'FullyQualifiedName~PaperSpecTests.The_paper_design_table_matches_the_spec' --nologo --verbosity quiet
    $code = $LASTEXITCODE
} finally {
    Remove-Item Env:MARQORA_WRITE_PAPER_DOCS -ErrorAction SilentlyContinue
}

if ($code -eq 0) {
    if ($Check) { 'The paper design document matches the spec.' } else { 'The paper design document is up to date with the spec.' }
} else {
    'The paper design document has fallen behind the spec. Run: pwsh ./build/Update-PaperDesign.ps1'
}

exit $code
