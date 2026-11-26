#Requires -Version 5.1
<#
.SYNOPSIS
    MindAttic.Media.Capture's run menu: 1) Redeploy  2) Run Debug  3) Run Release  (the MindAttic standard, MAT-T279).

.DESCRIPTION
    A thin wrapper over the shared deploy-lib.ps1 (Invoke-AppRun, MAT-T300). run.bat beside it (written by
    deploy-lib.ps1's Write-RunBat) opens the same menu on a double-click.
      1) Redeploy     builds the capture agent (capture.exe) and the tray recorder (capture-tray.exe) to
                      R:\MindAttic\Apps\MindAttic.Media.Capture\{Debug,Release}, both framework-dependent
      2) Run Debug    R:\...\Debug\capture-tray.exe in this console; extra arguments pass through
      3) Run Release  R:\...\Release\capture-tray.exe, started the way a double-click would start it
    Picks combine: 13 is Deploy & Run. Unattended callers (agents, /deploy) pass -Redeploy: no switch opens the menu.

    Capture's own differences, in the override block below:
      - Plan publishes the agent and tray projects only, not single-file (the agent's Playwright driver sits beside
        it in .playwright).
      - PostBuild runs the tray with --write-settings-manifest so MindAttic Vault shows this build's settings.
      - After the Release build it stops the tray running from %LOCALAPPDATA%\MindAttic\Capture\bin, installs that
        build there (tools\install.ps1 -From: DesktopCapture finds capture.exe there, and the "MindAttic Media
        Capture" desktop shortcut opens the tray from there), restarts the tray if it was running, and removes the
        old launcher folders (C:\Apps or %LOCALAPPDATA%\Programs, as MindAttic.Media.Capture or MindAttic.Capture).

    Callers from before the shared menu: -Launch means -Redeploy -RunRelease, -Force means -Redeploy.

.EXAMPLE
    .\run.ps1                    the menu
    .\run.ps1 -Redeploy          build both configs and install the Release build
    .\run.ps1 -Redeploy -RunRelease -DryRun
#>
param(
    [switch]$Redeploy,
    [switch]$RunDebug,
    [switch]$RunRelease,
    [switch]$DryRun,
    [switch]$Launch,
    [switch]$Force,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$AppArgs = @()
)
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$Override = @{
    Exe       = 'capture-tray.exe'
    Plan      = @{
        Kind    = 'script'
        Command = 'dotnet publish src\MindAttic.Media.Capture.Agent\MindAttic.Media.Capture.Agent.csproj -c {config} -o "{out}" -nologo -v q && dotnet publish src\MindAttic.Media.Capture.Tray\MindAttic.Media.Capture.Tray.csproj -c {config} -o "{out}" -nologo -v q'
    }
    PostBuild = {
        param($Config, $Out)
        $manifest = Start-Process -FilePath (Join-Path $Out 'capture-tray.exe') -ArgumentList '--write-settings-manifest' -WorkingDirectory $Out -Wait -PassThru
        if ($manifest.ExitCode -ne 0) { Write-Host "  (--write-settings-manifest exited $($manifest.ExitCode); the tray writes it on its next start)" -ForegroundColor DarkYellow }
        if ($Config -ne 'Release') { return }

        $bin = Join-Path $env:LOCALAPPDATA 'MindAttic\Capture\bin'
        $tray = Join-Path $bin 'capture-tray.exe'
        $installedTray = @(Get-Process 'capture-tray' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $tray })
        $installedTray | Stop-Process -Force
        & (Join-Path $repoRoot 'tools\install.ps1') -From $Out
        if ($installedTray.Count) { Start-Process -FilePath $tray -WorkingDirectory $bin }

        foreach ($apps in 'C:\Apps', (Join-Path $env:LOCALAPPDATA 'Programs')) {
            foreach ($oldName in 'MindAttic.Media.Capture', 'MindAttic.Capture') {
                $oldDir = Join-Path $apps $oldName
                if (Test-Path -LiteralPath $oldDir) {
                    Remove-Item -LiteralPath $oldDir -Recurse -Force
                    Write-Host "  Removed old launcher folder $oldDir" -ForegroundColor DarkGray
                }
            }
        }
    }
}

function Find-DeployLib {
    if ($env:MINDATTIC_DEPLOY_LIB -and (Test-Path -LiteralPath $env:MINDATTIC_DEPLOY_LIB)) { return $env:MINDATTIC_DEPLOY_LIB }
    $roots = @()
    $d = $PSScriptRoot
    while ($d) { $roots += $d; $d = Split-Path -Parent $d }
    $roots += 'D:\Projects\MindAttic'
    foreach ($r in $roots) {
        foreach ($rel in 'MindAttic.Agency\shared\deploy-lib.ps1', 'mindattic-agent-standard\shared\deploy-lib.ps1') {
            $p = Join-Path $r $rel
            if (Test-Path -LiteralPath $p) { return $p }
        }
    }
    throw 'deploy-lib.ps1 not found: clone MindAttic.Agency beside this repo, or set MINDATTIC_DEPLOY_LIB to its path.'
}

. (Find-DeployLib)
Invoke-AppRun -Repo $PSScriptRoot -Redeploy:($Redeploy -or $Launch -or $Force) -RunDebug:$RunDebug -RunRelease:($RunRelease -or $Launch) `
    -DryRun:$DryRun -AppArgs $AppArgs -Override $Override
