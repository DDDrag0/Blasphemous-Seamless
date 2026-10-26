<#
.SYNOPSIS
    Installs the verified mod loader stack for Blasphemous (BepInEx 5 + Modding API + Cheat Console).

.DESCRIPTION
    Installs the three runtime components the multiplayer mod needs from official GitHub releases,
    verifying every download against a SHA256 hash taken from the release manifest. Backs up any
    file it is about to overwrite and prints the commands to undo everything.

.PARAMETER GamePath
    The Blasphemous installation to install into. Defaults to the real install (D:).

.PARAMETER CachePath
    Where verified zips are downloaded and kept. Re-running reuses them.

.PARAMETER WhatIf
    Print the plan without changing anything.

.EXAMPLE
    pwsh -NoProfile -File install-mod-loader.ps1
    pwsh -NoProfile -File install-mod-loader.ps1 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$GamePath = 'D:\SteamLibrary\steamapps\common\Blasphemous',
    [string]$CachePath = (Join-Path $PSScriptRoot 'cache'),

    # Only set this if you know you want a non-default game folder.
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# SHA256 values read from the GitHub release manifests of each project.
$components = @(
    @{
        Name = 'BepInEx 5.4.23.5 (x64)'
        Url  = 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip'
        File = 'BepInEx_win_x64_5.4.23.5.zip'
        Sha  = '82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4'
        Sub  = 'bepinex'
    },
    @{
        Name = 'Blasphemous Modding API 3.0.1'
        Url  = 'https://github.com/BrandenEK/Blasphemous.ModdingAPI/releases/download/3.0.1/ModdingAPI.zip'
        File = 'ModdingAPI.zip'
        Sha  = 'b74fe3900b563483d090fd7f01939fe7d60ba5711e83d7996396a274e7156ff7'
        Sub  = 'moddingapi'
    },
    @{
        Name = 'Blasphemous Cheat Console 1.1.0'
        Url  = 'https://github.com/BrandenEK/Blasphemous.CheatConsole/releases/download/1.1.0/CheatConsole.zip'
        File = 'CheatConsole.zip'
        Sha  = '2280e5b4fd82a88395aae5f841a4b64c47cf24a22c0603f02efb220662067db8'
        Sub  = 'cheatconsole'
    }
)

function Write-Step($text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Write-Ok($text)   { Write-Host "    $text" -ForegroundColor Green }
function Write-Warn($text) { Write-Host "    $text" -ForegroundColor Yellow }

# Copies a directory tree into an existing tree, backing up files it replaces. Merging (not
# replacing whole folders) matters: several components contribute to BepInEx\plugins, and moving
# that folder aside would delete previously installed plugins.
function Install-Tree {
    param([string]$Source, [string]$Destination, [string]$BackupRoot, [string]$Stamp)

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null

    foreach ($item in Get-ChildItem -Path $Source -Force) {
        $target = Join-Path $Destination $item.Name

        if ($item.PSIsContainer) {
            Install-Tree -Source $item.FullName -Destination $target -BackupRoot $BackupRoot -Stamp $Stamp
            continue
        }

        if (Test-Path $target) {
            New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
            $backup = Join-Path $BackupRoot ($item.Name + '.' + $Stamp)
            if ($PSCmdlet.ShouldProcess($target, "back up to $backup")) {
                Copy-Item -Path $target -Destination $backup -Force
                Write-Warn "backed up existing $($item.Name) -> $backup"
            }
        }

        if ($PSCmdlet.ShouldProcess($target, "install")) {
            Copy-Item -Path $item.FullName -Destination $target -Force
        }
    }
}

Write-Step "Checking game folder"
if (-not (Test-Path $GamePath)) {
    throw "Game folder not found: $GamePath. Pass -GamePath with the correct path."
}

$exe = Join-Path $GamePath 'Blasphemous.exe'
$data = Join-Path $GamePath 'Blasphemous_Data'
if (-not (Test-Path $exe) -or -not (Test-Path $data)) {
    if (-not $Force) {
        throw "'$GamePath' does not look like a Blasphemous install (no Blasphemous.exe / Blasphemous_Data). Use -Force to override."
    }
    Write-Warn "Path does not look like a Blasphemous install, continuing because -Force was given."
}
Write-Ok "Target: $GamePath"

# Already installed?
$alreadyPresent = @()
foreach ($marker in 'winhttp.dll', 'BepInEx\core\BepInEx.dll', 'doorstop_config.ini') {
    if (Test-Path (Join-Path $GamePath $marker)) { $alreadyPresent += $marker }
}
if ($alreadyPresent.Count -gt 0) {
    Write-Warn "Existing loader files detected: $($alreadyPresent -join ', ')"
    Write-Warn "They will be backed up before being replaced."
}

# Files that must be present after a successful install.
$expected = @(
    'winhttp.dll',
    'doorstop_config.ini',
    '.doorstop_version',
    'BepInEx\core\BepInEx.dll',
    'BepInEx\core\0Harmony.dll',
    'BepInEx\plugins\ModdingAPI.dll',
    'BepInEx\plugins\CheatConsole.dll'
)

if ($WhatIfPreference) {
    Write-Host ""
    Write-Host "Plan (nothing was changed):" -ForegroundColor Green
    Write-Host "  Install into : $GamePath"
    Write-Host "  Cache        : $CachePath"
    Write-Host "  Components   :"
    foreach ($c in $components) {
        Write-Host ("    - {0}" -f $c.Name)
        Write-Host ("        {0}" -f $c.Url)
        Write-Host ("        sha256 {0}" -f $c.Sha)
    }
    Write-Host "  Then verify  : $($expected -join ', ')"
    Write-Host ""
    Write-Host "Existing loader files that would be backed up and replaced:"
    if ($alreadyPresent.Count -gt 0) {
        foreach ($m in $alreadyPresent) { Write-Host "    - $m" }
    } else {
        Write-Host "    (none)"
    }
    Write-Host ""
    return
}

New-Item -ItemType Directory -Force -Path $CachePath | Out-Null
$backupRoot = Join-Path $CachePath ("backup-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

# Solution path for the "next steps" message: searched under $PSScriptRoot's parent, so it
# survives both nested and flattened layouts.
$RepoRoot = Split-Path -Parent $PSScriptRoot
$slnItem  = Get-ChildItem -LiteralPath $RepoRoot -Filter 'Blasphemous.Multiplayer.sln' -File -ErrorAction SilentlyContinue |
            Select-Object -First 1
if (-not $slnItem) {
    $slnItem = Get-ChildItem -LiteralPath $RepoRoot -Filter 'Blasphemous.Multiplayer.sln' -File -Recurse -ErrorAction SilentlyContinue |
               Select-Object -First 1
}
# When absent (loader-only install, incomplete repo) name the root instead of guessing.
$SolutionPath = if ($slnItem) { $slnItem.FullName } else { Join-Path $RepoRoot '<repo>\Blasphemous.Multiplayer.sln (not found)' }

Write-Step "Downloading and verifying"
foreach ($c in $components) {
    $zip = Join-Path $CachePath $c.File

    if (-not (Test-Path $zip)) {
        Write-Host "    downloading $($c.File)"
        Invoke-WebRequest -Uri $c.Url -OutFile $zip -UseBasicParsing
    } else {
        Write-Host "    reusing cached $($c.File)"
    }

    $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $c.Sha.ToLower()) {
        throw "HASH MISMATCH for $($c.File)`n  expected $($c.Sha)`n  actual   $actual`nRefusing to install. Delete the cached file and retry."
    }
    Write-Ok "$($c.Name): sha256 verified"
}

Write-Step "Extracting"
foreach ($c in $components) {
    $dir = Join-Path $CachePath $c.Sub
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    Expand-Archive (Join-Path $CachePath $c.File) -DestinationPath $dir -Force
    Write-Ok "$($c.Name) -> $dir"
}

# Where each component's top-level entries belong. The BepInEx zip already targets the game root;
# the mod zips contain a bare "plugins" folder that must go under BepInEx\, not at the game root,
# or the loader never looks at it.
$plan = @(
    @{ Sub = 'bepinex';      Desc = 'BepInEx loader';      Dest = '' }
    @{ Sub = 'moddingapi';   Desc = 'Modding API plugin';  Dest = 'BepInEx' }
    @{ Sub = 'cheatconsole'; Desc = 'Cheat Console plugin'; Dest = 'BepInEx' }
)

Write-Step "Installing into $GamePath"
foreach ($p in $plan) {
    $src = Join-Path $CachePath $p.Sub
    $dest = if ([string]::IsNullOrEmpty($p.Dest)) { $GamePath } else { Join-Path $GamePath $p.Dest }
    Install-Tree -Source $src -Destination $dest -BackupRoot $backupRoot -Stamp $stamp
    Write-Ok "$($p.Desc) installed"
}

Write-Step "Verifying installation"
$missing = @()
foreach ($rel in $expected) {
    if (Test-Path (Join-Path $GamePath $rel)) {
        Write-Ok "$rel"
    } else {
        Write-Warn "MISSING: $rel"
        $missing += $rel
    }
}

if ($missing.Count -gt 0) {
    Write-Host ""
    Write-Host "Install incomplete, missing: $($missing -join ', ')" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Mod loader installed successfully." -ForegroundColor Green
Write-Host ""
Write-Host "Next:"
Write-Host "  1. Rebuild and deploy the multiplayer mod:"
Write-Host "       dotnet build `"$SolutionPath`""
Write-Host "     (its post-build target copies BlasClient.dll into <game>\Modding\)"
Write-Host "  2. Launch Blasphemous through Steam."
Write-Host "  3. Confirm the log <game>\BepInEx\LogOutput.log mentions the loaded plugins."
Write-Host "  4. Then follow docs\ENEMY_SYNC_TESTPLAN.md."
Write-Host ""
Write-Host "To undo this installation:"
if (Test-Path $backupRoot) {
    Write-Host "    # original files that were replaced are in $backupRoot"
}
Write-Host "    Remove-Item '$GamePath\BepInEx' -Recurse -Force"
Write-Host "    Remove-Item '$GamePath\winhttp.dll' -Force"
Write-Host "    Remove-Item '$GamePath\doorstop_config.ini' -Force"
Write-Host "    Remove-Item '$GamePath\.doorstop_version' -Force"
if (Test-Path $backupRoot) {
    Write-Host "    # then copy anything you need back from: $backupRoot"
}