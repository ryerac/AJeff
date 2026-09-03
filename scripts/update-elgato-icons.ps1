[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryUri = 'https://github.com/elgatosf/icons.git'
$repoRoot = Split-Path -Parent $PSScriptRoot
$packsRoot = Join-Path $repoRoot 'JeffDock.App\Assets\Icons\Packs'
$destination = Join-Path $packsRoot 'Elgato'
$runId = [guid]::NewGuid().ToString('N')
$downloadRoot = Join-Path ([System.IO.Path]::GetTempPath()) "jeffdock-elgato-$runId"
$stagedPack = Join-Path $packsRoot ".Elgato-update-$runId"
$backupPack = Join-Path $packsRoot ".Elgato-backup-$runId"
$backupCreated = $false
$destinationInstalled = $false

function Remove-DirectoryIfPresent {
    param([Parameter(Mandatory)][string] $Path)

    if (Test-Path -LiteralPath $Path -PathType Container) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'Git is required but was not found on PATH.'
    }

    New-Item -ItemType Directory -Path $packsRoot -Force | Out-Null

    Write-Host 'Downloading the latest Elgato icon repository...'
    & git clone --depth 1 --quiet $repositoryUri $downloadRoot
    if ($LASTEXITCODE -ne 0) {
        throw "git clone failed with exit code $LASTEXITCODE."
    }

    $svgSource = Join-Path $downloadRoot 'svg\l'
    $licenseSource = Join-Path $downloadRoot 'LICENSE'
    $packageSource = Join-Path $downloadRoot 'package.json'
    $svgFiles = @(Get-ChildItem -LiteralPath $svgSource -Filter '*.svg' -File)

    if ($svgFiles.Count -eq 0) {
        throw "No large SVG icons were found at '$svgSource'. Upstream layout may have changed."
    }
    if (-not (Test-Path -LiteralPath $licenseSource -PathType Leaf)) {
        throw 'The upstream MIT license file was not found.'
    }
    if (-not (Test-Path -LiteralPath $packageSource -PathType Leaf)) {
        throw 'The upstream package metadata was not found.'
    }

    $package = Get-Content -LiteralPath $packageSource -Raw | ConvertFrom-Json
    $version = [string]$package.version
    $commit = (& git -C $downloadRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') {
        throw 'Could not determine the downloaded Git commit.'
    }

    Write-Host "Staging $($svgFiles.Count) SVG icons (version $version, commit $($commit.Substring(0, 12)))..."
    $stagedGeneral = Join-Path $stagedPack 'General'
    New-Item -ItemType Directory -Path $stagedGeneral -Force | Out-Null
    $svgFiles | Copy-Item -Destination $stagedGeneral
    Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $stagedPack 'LICENSE.txt')

    $manifest = [ordered]@{
        id = 'elgato'
        name = 'Elgato Icons'
        version = $version
        author = 'Elgato / Corsair Memory Inc.'
        license = 'MIT'
        source = 'https://github.com/elgatosf/icons'
        commit = $commit
    }
    $manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stagedPack 'pack.json') -Encoding utf8

    $readme = @"
# Elgato Icons

This directory contains the large (``svg/l``) variants from
[`elgatosf/icons`](https://github.com/elgatosf/icons), pinned at commit
``$commit`` (package version $version).

The assets are bundled so the JeffDock icon library works without an internet
connection. They are licensed under the MIT License; see ``LICENSE.txt`` in this
directory.
"@
    Set-Content -LiteralPath (Join-Path $stagedPack 'README.md') -Value $readme -Encoding utf8

    if (Test-Path -LiteralPath $destination) {
        Move-Item -LiteralPath $destination -Destination $backupPack
        $backupCreated = $true
    }

    try {
        Move-Item -LiteralPath $stagedPack -Destination $destination
        $destinationInstalled = $true
    }
    catch {
        if ($backupCreated -and -not (Test-Path -LiteralPath $destination)) {
            Move-Item -LiteralPath $backupPack -Destination $destination
            $backupCreated = $false
        }
        throw
    }

    Remove-DirectoryIfPresent -Path $backupPack
    $backupCreated = $false

    Write-Host ''
    Write-Host 'Elgato icons updated successfully.' -ForegroundColor Green
    Write-Host "  Version: $version"
    Write-Host "  Commit:  $commit"
    Write-Host "  Icons:   $($svgFiles.Count)"
    Write-Host "  Path:    $destination"
}
finally {
    Remove-DirectoryIfPresent -Path $downloadRoot
    Remove-DirectoryIfPresent -Path $stagedPack

    if ($backupCreated -and $destinationInstalled) {
        Remove-DirectoryIfPresent -Path $backupPack
    }
}
