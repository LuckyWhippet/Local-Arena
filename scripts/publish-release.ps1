<#
.SYNOPSIS
Creates or completes a GitHub release and uploads the packaged update assets.

.DESCRIPTION
scripts/package.ps1 only writes files into artifacts\; nothing in the repository
can reach GitHub without a credential. This script does the publishing half: it
finds or creates the release for the manifest version, uploads every asset that
is missing, and optionally sets the repository description and topics.

The token needs Contents: write for the release upload and, unless
-SkipRepositoryMetadata is passed, Administration: write for the topics.

.EXAMPLE
$env:GITHUB_TOKEN = "ghp_..."
./scripts/publish-release.ps1
#>
[CmdletBinding()]
param(
    [string]$Token = $(if ($env:GITHUB_TOKEN) { $env:GITHUB_TOKEN } else { $env:GH_TOKEN }),
    [string]$Repository = "LuckyWhippet/Local-Arena",
    [string]$ArtifactDirectory,
    [string]$ReleaseTag,
    [string]$NotesPath,
    [string]$Description = "Windows toolkit for local CS2 matches: player cosmetics, enhanced bots, demos, diagnostics and managed installation. Community continuation fork of Local Arena.",
    [string[]]$Topics = @("counter-strike-2", "cs2", "counterstrikesharp", "tauri", "rust", "game-mod", "cosmetics", "bots", "windows"),
    [switch]$SkipRepositoryMetadata
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ArtifactDirectory) { $ArtifactDirectory = Join-Path $repoRoot "artifacts" }
$ArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory).Path

if ([string]::IsNullOrWhiteSpace($Token)) {
    throw 'No GitHub token. Set $env:GITHUB_TOKEN or pass -Token. Creating a release requires Contents: write.'
}

$apiBase = "https://api.github.com/repos/$Repository"
$headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
    "User-Agent"           = "localarena-publish"
}

function Invoke-GitHub {
    param(
        [Parameter(Mandatory)] [string]$Method,
        [Parameter(Mandatory)] [string]$Uri,
        $Body,
        [switch]$Allowed404
    )
    $parameters = @{ Method = $Method; Uri = $Uri; Headers = $headers }
    if ($null -ne $Body) {
        $parameters.Body = ($Body | ConvertTo-Json -Depth 10 -Compress)
        $parameters.ContentType = "application/json; charset=utf-8"
    }
    try {
        return Invoke-RestMethod @parameters
    } catch {
        $status = $null
        if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
        if ($Allowed404 -and $status -eq 404) { return $null }
        $detail = $_.ErrorDetails.Message
        if (-not $detail) { $detail = $_.Exception.Message }
        throw "GitHub $Method $Uri failed (HTTP $status): $detail"
    }
}

$manifestPath = Join-Path $ArtifactDirectory "latest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) {
    throw "Missing $manifestPath. Run scripts/package.ps1 first."
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$version = [string]$manifest.release_version
if (-not $ReleaseTag) { $ReleaseTag = "v$version" }
if (-not $NotesPath) {
    $candidate = Join-Path $repoRoot "RELEASE-NOTES-$version.md"
    if (Test-Path -LiteralPath $candidate) { $NotesPath = $candidate }
}

$releaseBody = $null
if ($NotesPath -and (Test-Path -LiteralPath $NotesPath)) {
    $releaseBody = Get-Content -LiteralPath $NotesPath -Raw
}

Write-Host "Repository : $Repository"
Write-Host "Tag        : $ReleaseTag"
Write-Host "Version    : $version"
Write-Host "Artifacts  : $ArtifactDirectory"

$release = Invoke-GitHub -Method GET -Uri "$apiBase/releases/tags/$ReleaseTag" -Allowed404
if ($null -eq $release) {
    $payload = @{
        tag_name               = $ReleaseTag
        name                   = "Local Arena $ReleaseTag"
        draft                  = $false
        prerelease             = $false
        generate_release_notes = $false
    }
    if ($releaseBody) { $payload.body = $releaseBody }
    Write-Host "Creating release $ReleaseTag ..."
    $release = Invoke-GitHub -Method POST -Uri "$apiBase/releases" -Body $payload
} else {
    Write-Host "Release $ReleaseTag already exists; uploading any missing assets." -ForegroundColor Yellow
    if ($releaseBody -and ([string]$release.body -ne $releaseBody)) {
        $release = Invoke-GitHub -Method PATCH -Uri "$apiBase/releases/$($release.id)" -Body @{ body = $releaseBody }
        Write-Host "Release notes refreshed from $NotesPath"
    }
}

$assetFiles = @(
    "LocalArena-v$version-windows.zip"
    "LocalArena-panel-v$version-windows.zip"
    "LocalArena-plugin-v$version-windows.zip"
    "latest.json"
    "latest.json.sig"
    "SHA256SUMS.txt"
)

$existing = @{}
foreach ($asset in @($release.assets)) { $existing[[string]$asset.name] = $true }

$client = [System.Net.Http.HttpClient]::new()
try {
    $client.Timeout = [TimeSpan]::FromMinutes(45)
    $client.DefaultRequestHeaders.Authorization =
        [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $Token)
    $client.DefaultRequestHeaders.Add("User-Agent", "localarena-publish")
    $client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json")
    $client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28")

    $missing = @()
    foreach ($name in $assetFiles) {
        $path = Join-Path $ArtifactDirectory $name
        if (-not (Test-Path -LiteralPath $path)) { $missing += $name; continue }
        if ($existing.ContainsKey($name)) {
            Write-Host "  present  $name"
            continue
        }
        $size = (Get-Item -LiteralPath $path).Length
        Write-Host ("  uploading {0} ({1:N1} MiB) ..." -f $name, ($size / 1MB))
        $stream = [IO.File]::OpenRead($path)
        try {
            $content = [System.Net.Http.StreamContent]::new($stream)
            $content.Headers.ContentType =
                [System.Net.Http.Headers.MediaTypeHeaderValue]::new("application/octet-stream")
            $uploadUri = "https://uploads.github.com/repos/$Repository/releases/$($release.id)/assets?name=$name"
            $response = $client.PostAsync($uploadUri, $content).GetAwaiter().GetResult()
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) {
                throw "Upload of $name failed (HTTP $([int]$response.StatusCode)): $text"
            }
        } finally {
            $stream.Dispose()
        }
    }
    if ($missing.Count -gt 0) {
        Write-Warning "Assets not found in $($ArtifactDirectory): $($missing -join ', ')"
    }
} finally {
    $client.Dispose()
}

if (-not $SkipRepositoryMetadata) {
    Write-Host "Updating repository description and topics ..."
    Invoke-GitHub -Method PATCH -Uri $apiBase -Body @{ description = $Description } | Out-Null
    Invoke-GitHub -Method PUT -Uri "$apiBase/topics" -Body @{ names = $Topics } | Out-Null
}

Write-Host ""
Write-Host "Release page : https://github.com/$Repository/releases/tag/$ReleaseTag"
Write-Host "Update feed  : https://github.com/$Repository/releases/latest/download/latest.json"
