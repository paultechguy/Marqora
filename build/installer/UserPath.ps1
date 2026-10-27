# The user's PATH, for Install.ps1 and Uninstall.ps1: the one copy of how it is read and changed.
# Dot-sourced by both, and installed beside the uninstaller so it is there when that runs.
#
# Windows PowerShell 5.1 compatible, like the scripts that use it.
#
# The registry value itself rather than [Environment]::GetEnvironmentVariable and
# SetEnvironmentVariable, which is the obvious way and the wrong one. Get returns PATH with
# %USERPROFILE% and its like already expanded, so writing that back would freeze every such entry
# to what it happens to mean today - a change to entries this installer never made. Here the value
# is read unexpanded, changed by the one entry only, written back as REG_EXPAND_SZ, and Windows is
# told the environment changed, so a terminal opened afterwards sees it.
#
# Empty segments are kept as they were: "the rest of PATH as it was" means the text, not a tidier
# version of it (CLI-PKG-02).

$userEnvironmentKey = 'HKCU:\Environment'

function Read-UserPath {
    $key = Get-Item -LiteralPath $userEnvironmentKey
    return [string] $key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
}

function Write-UserPath {
    param([string] $Value)

    if ($Value) {
        Set-ItemProperty -LiteralPath $userEnvironmentKey -Name 'Path' -Value $Value -Type ExpandString
    }
    else {
        Remove-ItemProperty -LiteralPath $userEnvironmentKey -Name 'Path' -ErrorAction SilentlyContinue
    }

    Send-EnvironmentChange
}

function Test-SamePathEntry {
    param([string] $Entry, [string] $Directory)

    $expanded = [Environment]::ExpandEnvironmentVariables($Entry.Trim()).TrimEnd('\')
    return [string]::Equals($expanded, $Directory.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}

function Test-UserPathEntry {
    param([string] $Directory)

    return @((Read-UserPath) -split ';' | Where-Object { $_ -and (Test-SamePathEntry -Entry $_ -Directory $Directory) }).Count -gt 0
}

# True when the entry was added; false when PATH already had it, which then stays the user's own
# and is never removed by the uninstaller.
function Add-UserPathEntry {
    param([string] $Directory)

    if (Test-UserPathEntry -Directory $Directory) {
        return $false
    }

    $current = Read-UserPath
    $separator = if (-not $current -or $current.EndsWith(';')) { '' } else { ';' }

    Write-UserPath -Value ($current + $separator + $Directory)
    return $true
}

# True when an entry was removed. Only entries naming exactly this folder go; everything else,
# order and spelling included, stays as it was.
function Remove-UserPathEntry {
    param([string] $Directory)

    $segments = @((Read-UserPath) -split ';')
    $kept = @($segments | Where-Object { -not ($_ -and (Test-SamePathEntry -Entry $_ -Directory $Directory)) })

    if ($kept.Count -eq $segments.Count) {
        return $false
    }

    Write-UserPath -Value ($kept -join ';')
    return $true
}

function Send-EnvironmentChange {
    $signature = @'
[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
'@

    try {
        $user32 = Add-Type -MemberDefinition $signature -Name 'MarqoraEnvironmentNotify' -Namespace 'Marqora' -PassThru
        $result = [UIntPtr]::Zero

        # HWND_BROADCAST, WM_SETTINGCHANGE "Environment", SMTO_ABORTIFHUNG, five seconds at most.
        [void] $user32::SendMessageTimeout([IntPtr] 0xffff, 0x001A, [UIntPtr]::Zero, 'Environment', 0x0002, 5000, [ref] $result)
    }
    catch {
        # The value is written either way; this only asks Explorer to notice before the next sign-in.
    }
}
