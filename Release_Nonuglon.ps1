<#
.SYNOPSIS
  Bumps Nonuglon's version, tags it, and pushes the tag - which triggers
  .github/workflows/release.yml to build, publish a GitHub Release, and sync
  repo.json.

.PARAMETER Bump
  "patch" (default), "minor", "major", or an explicit version like "1.4.0".

.PARAMETER DryRun
  Show what would happen without actually tagging or pushing anything.

.EXAMPLE
  .\Release_Nonuglon.ps1
  Bumps the patch version (e.g. v0.1.0 -> v0.1.1) and releases it.

.EXAMPLE
  .\Release_Nonuglon.ps1 minor
  Bumps the minor version (e.g. v0.1.3 -> v0.2.0) and releases it.

.EXAMPLE
  .\Release_Nonuglon.ps1 -DryRun
  Shows the next version and runs the build-sanity-check, but doesn't tag/push.
#>
param(
    [string]$Bump = "patch",
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"
$repoPath = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $repoPath

Write-Host "== Nonuglon release ==" -ForegroundColor Cyan

# -- Working tree must be clean, so we never tag a commit that doesn't match
#    what's actually on GitHub. --
$status = git status --porcelain
if ($status) {
    Write-Host "Working tree has uncommitted changes - commit or stash first:" -ForegroundColor Red
    Write-Host $status
    exit 1
}

# -- Local master must match origin/master, for the same reason. --
git fetch origin master --quiet
$local = git rev-parse HEAD
$remote = git rev-parse origin/master
if ($local -ne $remote) {
    Write-Host "Local master doesn't match origin/master - push or pull first." -ForegroundColor Red
    exit 1
}

# -- Figure out the next version. --
git fetch --tags origin --quiet
$lastTag = git describe --tags --abbrev=0 2>$null
if (-not $lastTag) { $lastTag = "v0.0.0" }

$parts = $lastTag.TrimStart('v').Split('.')
[int]$major = $parts[0]
[int]$minor = $parts[1]
[int]$patch = $parts[2]

if ($Bump -match '^\d+\.\d+\.\d+$') {
    $newVersion = $Bump
}
elseif ($Bump -eq "major") {
    $major++; $minor = 0; $patch = 0
    $newVersion = "$major.$minor.$patch"
}
elseif ($Bump -eq "minor") {
    $minor++; $patch = 0
    $newVersion = "$major.$minor.$patch"
}
else {
    $patch++
    $newVersion = "$major.$minor.$patch"
}

$newTag = "v$newVersion"

Write-Host "Last release: $lastTag"
Write-Host "New release:  $newTag"

# -- Sanity-check the build BEFORE tagging, so a broken build never gets
#    published as a release. Same version-stamping the real workflow uses. --
Write-Host "Build sanity check..." -ForegroundColor Cyan
$buildOutput = Join-Path $repoPath "_release_check"
if (Test-Path $buildOutput) { Remove-Item -Recurse -Force $buildOutput }
dotnet build "$repoPath\Nonuglon\Nonuglon.csproj" -c Release -p:Version=$newVersion -o $buildOutput
if ($LASTEXITCODE -ne 0) {
    Remove-Item -Recurse -Force $buildOutput -ErrorAction SilentlyContinue
    Write-Host "Build failed - not tagging." -ForegroundColor Red
    exit 1
}
Remove-Item -Recurse -Force $buildOutput

if ($DryRun) {
    Write-Host "Dry run - would tag and push $newTag now. Nothing done." -ForegroundColor Yellow
    exit 0
}

$confirm = Read-Host "Tag and push $newTag ? (y/N)"
if ($confirm -ne 'y') {
    Write-Host "Aborted."
    exit 0
}

git tag -a $newTag -m "Release $newTag"
git push origin $newTag

Write-Host "Pushed $newTag - GitHub Actions will build, publish the release, and update repo.json." -ForegroundColor Green
