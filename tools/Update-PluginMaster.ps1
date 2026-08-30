<#
.SYNOPSIS
    Rebuilds the plugins and regenerates pluginmaster.json.

.DESCRIPTION
    Reads tools/sources.json and produces the single manifest Dalamud reads.

    Plugins hosted "dist" are built here: the packaged zip is copied into dist/ and the
    version, description and tags come straight out of the manifest DalamudPackager
    generated, so the advertised version can never drift from the shipped binary.

    Plugins hosted "external" keep their own repo and their own releases. Their entry is
    pulled live from that repo's repo.json, which stays the source of truth for them.

.PARAMETER SkipBuild
    Reuse whatever is already in each project's bin/Release instead of rebuilding.

.PARAMETER Push
    Commit the result and push to origin.

.EXAMPLE
    .\tools\Update-PluginMaster.ps1
    .\tools\Update-PluginMaster.ps1 -SkipBuild -Push
#>
[CmdletBinding()]
param(
    [switch] $SkipBuild,
    [switch] $Push
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$distDir  = Join-Path $repoRoot 'dist'
$config   = Get-Content (Join-Path $PSScriptRoot 'sources.json') -Raw -Encoding UTF8 | ConvertFrom-Json

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

function Write-Utf8NoBom {
    param([string] $Path, [string] $Content)
    [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding $false))
}

# Dalamud compares the advertised version against the assembly version as strings, so a
# release tagged "v0.1.2" against an assembly of "0.1.2.0" reads as a permanent pending
# update. Pad everything to four parts.
function Normalize-Version {
    param([string] $Version)
    if ([string]::IsNullOrWhiteSpace($Version)) { return '0.0.0.0' }
    $v = $Version.TrimStart('v', 'V')
    $parts = @($v -split '\.')
    while ($parts.Count -lt 4) { $parts += '0' }
    return ($parts[0..3] -join '.')
}

function Get-DistEntry {
    param($Plugin)

    $name = $Plugin.InternalName

    if (-not $SkipBuild) {
        Write-Host "  building..." -ForegroundColor DarkGray
        # The packager refuses to overwrite a read-only output dir, which is how a folder
        # that has been open in Explorer or loaded as a dev plugin tends to end up.
        $projDir = Split-Path $Plugin.Project -Parent
        $stale = Join-Path $projDir "bin\Release\$name"
        if (Test-Path $stale) { Get-Item $stale -Force | ForEach-Object { $_.Attributes = 'Directory' } }

        & dotnet build $Plugin.Project -c Release --nologo | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "build failed: $($Plugin.Project)" }
    }

    $releaseDir = Join-Path (Split-Path $Plugin.Project -Parent) 'bin\Release'
    $manifest   = Join-Path $releaseDir "$name.json"
    $zip        = Join-Path $releaseDir "$name\latest.zip"

    if (-not (Test-Path $manifest)) { throw "no manifest for ${name}: $manifest" }
    if (-not (Test-Path $zip))      { throw "no zip for ${name}: $zip" }

    Copy-Item $zip (Join-Path $distDir "$name.zip") -Force

    $m    = Get-Content $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
    $link = "$($config.RawBase)/$name.zip"

    $entry = [ordered]@{
        Author              = $m.Author
        Name                = $m.Name
        InternalName        = $name
        AssemblyVersion     = Normalize-Version $m.AssemblyVersion
        Description         = $m.Description
        Punchline           = $m.Punchline
        ApplicableVersion   = 'any'
        DalamudApiLevel     = $m.DalamudApiLevel
        Tags                = @($m.Tags)
        CategoryTags        = @(if ($Plugin.CategoryTags) { $Plugin.CategoryTags } else { @('other') })
        RepoUrl             = if ($Plugin.RepoUrl) { $Plugin.RepoUrl } else { $config.HomeRepo }
        IconUrl             = if ($Plugin.IconUrl) { $Plugin.IconUrl } else { '' }
        Changelog           = if ($Plugin.Changelog) { $Plugin.Changelog } else { '' }
        IsHide              = $false
        IsTestingExclusive  = [bool] $Plugin.IsTestingExclusive
        AcceptsFeedback     = [bool] $m.AcceptsFeedback
        LastUpdate          = [int64] (Get-Item $zip).LastWriteTimeUtc.Subtract([datetime]'1970-01-01').TotalSeconds
        DownloadLinkInstall = $link
        DownloadLinkUpdate  = $link
        DownloadLinkTesting = $link
    }

    Write-Host ("  {0} {1}  ({2:N2} MB)" -f $entry.Name, $entry.AssemblyVersion, ((Get-Item $zip).Length / 1MB)) -ForegroundColor Green
    return $entry
}

function Get-ExternalEntry {
    param($Plugin)

    $name = $Plugin.InternalName
    $raw  = Invoke-WebRequest $Plugin.Manifest -UseBasicParsing -TimeoutSec 30
    $doc  = $raw.Content | ConvertFrom-Json

    # A plugin's own repo.json is an array; take the entry matching this InternalName.
    $entry = @($doc) | Where-Object { $_.InternalName -eq $name } | Select-Object -First 1
    if (-not $entry) { throw "$name not found in $($Plugin.Manifest)" }

    # The published tag is more trustworthy than a hand-edited version field.
    if ($Plugin.ReleaseRepo) {
        $tag = & gh release view --repo $Plugin.ReleaseRepo --json tagName 2>$null
        if ($LASTEXITCODE -eq 0 -and $tag) {
            $tagName = ($tag | ConvertFrom-Json).tagName
            $fromTag = Normalize-Version $tagName
            $stated  = Normalize-Version $entry.AssemblyVersion
            if ($fromTag -ne $stated) {
                Write-Host "  version: repo.json says $stated, latest release is $fromTag - using the release" -ForegroundColor Yellow
            }
            $entry.AssemblyVersion = $fromTag
        }
    }

    $entry.AssemblyVersion = Normalize-Version $entry.AssemblyVersion
    Write-Host ("  {0} {1}  (external)" -f $entry.Name, $entry.AssemblyVersion) -ForegroundColor Green
    return $entry
}

$entries = @()
foreach ($plugin in $config.Plugins) {
    Write-Host "$($plugin.InternalName)" -ForegroundColor Cyan
    $entries += if ($plugin.Hosted -eq 'external') { Get-ExternalEntry $plugin } else { Get-DistEntry $plugin }
}

$json = $entries | ConvertTo-Json -Depth 12
Write-Utf8NoBom (Join-Path $repoRoot 'pluginmaster.json') $json

Write-Host ""
Write-Host "pluginmaster.json written - $($entries.Count) plugins" -ForegroundColor Cyan

if ($Push) {
    Push-Location $repoRoot
    try {
        git add -A
        git commit -m "Update plugin manifest" | Out-Null
        git push
        Write-Host "pushed" -ForegroundColor Cyan
    } finally { Pop-Location }
}
