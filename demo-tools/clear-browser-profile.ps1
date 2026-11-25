<#
Clears the saved browser sign-in of the Tracto demo: the Chromium profile folder that the browser
runtime and the Designer's "Sign in" button share.

This is a stand-alone tool. It is not part of the Tracto application and nothing in the application
calls it. It deletes one folder inside the browser profile root and nothing else.

What the sign-in is:  not data in the database. It is a folder of files (cookies, local storage, cache)
at   %LOCALAPPDATA%\Tracto\browser-profiles\<name>     (or under BROWSER_PROFILE_ROOT when that is set).
The default profile is named "default". Deleting the folder signs every site out of that profile; the
browser creates an empty folder again the next time it uses the profile. Sign in again afterwards
(Designer toolbar, Browser group, Sign in).

Not touched:  the database, the downloaded Chromium (%LOCALAPPDATA%\ms-playwright), other profiles
              (unless -All), the Microsoft sign-in of the sheet service (that one is in the database).

Run from anywhere:
  powershell -File demo-tools\clear-browser-profile.ps1             asks for confirmation
  powershell -File demo-tools\clear-browser-profile.ps1 -Yes        no question
  powershell -File demo-tools\clear-browser-profile.ps1 -WhatIf     only shows what would be deleted
  -Profile name                                                    another profile than "default"
  -All                                                             every profile under the root

Close every browser window that uses the profile first. The tool refuses while one is open.
#>
param(
    [string]$Profile = 'default',
    [switch]$All,
    [switch]$Yes,
    [switch]$WhatIf
)

$ErrorActionPreference = 'Continue'

function Fail([string]$Message) {
    Write-Host $Message -ForegroundColor Red
    exit 1
}

# Same location rule as the browser runtime and the Designer.
$root = $env:BROWSER_PROFILE_ROOT
if ([string]::IsNullOrWhiteSpace($root)) {
    $root = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Tracto\browser-profiles'
}
$rootFull = [IO.Path]::GetFullPath($root).TrimEnd('\')

$namePattern = '^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$'

if (-not (Test-Path -LiteralPath $rootFull)) {
    Write-Host "There is no profile folder yet ($rootFull). Nothing to clear." -ForegroundColor Green
    exit 0
}

if ($All) {
    $folders = @(Get-ChildItem -LiteralPath $rootFull -Directory -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
} else {
    if ($Profile -notmatch $namePattern) { Fail "A profile name uses letters, digits, dash, underscore or dot (1 to 64 characters): '$Profile'." }
    $folder = [IO.Path]::GetFullPath((Join-Path $rootFull $Profile))
    $folders = @()
    if (Test-Path -LiteralPath $folder) { $folders = @($folder) }
}

# Never delete anything outside the profile root.
foreach ($f in $folders) {
    if (-not $f.StartsWith($rootFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
        Fail "Refusing to touch '$f': it is outside the profile root '$rootFull'."
    }
}

if ($folders.Count -eq 0) {
    Write-Host "Profile '$Profile' does not exist under $rootFull. Nothing to clear." -ForegroundColor Green
    exit 0
}

# A browser that has the profile open would keep files locked and rewrite them on exit.
$running = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    $cmd = $_.CommandLine
    $cmd -and ($folders | Where-Object { $cmd.IndexOf($_, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
})
if ($running.Count -gt 0) {
    $names = ($running | ForEach-Object { "$($_.Name) (pid $($_.ProcessId))" } | Select-Object -Unique) -join ', '
    Fail "A browser is using the profile: $names. Close its window (or stop the browser runtime), then run this again."
}

Write-Host ''
Write-Host "Profile root: $rootFull" -ForegroundColor Cyan
$totalBytes = 0
foreach ($f in $folders) {
    $files = @(Get-ChildItem -LiteralPath $f -Recurse -File -Force -ErrorAction SilentlyContinue)
    $bytes = ($files | Measure-Object -Property Length -Sum).Sum
    if (-not $bytes) { $bytes = 0 }
    $totalBytes += $bytes
    $last = ($files | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
    Write-Host ('  {0,-30} {1,8:N1} MB   {2,6} files   last used {3}' -f (Split-Path $f -Leaf), ($bytes / 1MB), $files.Count, $last)
}
Write-Host ''
Write-Host ("{0} profile folder(s), {1:N1} MB, will be deleted. Every site in them is signed out." -f $folders.Count, ($totalBytes / 1MB)) -ForegroundColor Yellow

if ($WhatIf) { Write-Host 'Nothing was changed (-WhatIf).'; exit 0 }

if (-not $Yes) {
    $answer = Read-Host 'This cannot be undone. Type YES to delete them'
    if ($answer -cne 'YES') { Write-Host 'Cancelled. Nothing was changed.'; exit 2 }
}

foreach ($f in $folders) {
    Remove-Item -LiteralPath $f -Recurse -Force -ErrorAction SilentlyContinue
}

$left = @($folders | Where-Object { Test-Path -LiteralPath $_ })
if ($left.Count -gt 0) {
    Write-Host 'Some folders could not be deleted (a file is still in use?):' -ForegroundColor Red
    $left | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host 'Browser sign-in cleared. Sign in again from the Designer (Browser group, Sign in).' -ForegroundColor Green
exit 0
