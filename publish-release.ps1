[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[^/]+/[^/]+$')]
    [string]$Repository,
    [string]$Version = '0.4.0',
    [string]$RuntimeSource,
    [ValidateSet('public', 'private')]
    [string]$Visibility = 'public'
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh -and (Test-Path -LiteralPath 'C:\Program Files\GitHub CLI\gh.exe')) {
    $gh = Get-Item -LiteralPath 'C:\Program Files\GitHub CLI\gh.exe'
}
if ($null -eq $gh) { throw 'GitHub CLI is not installed. Install it with: winget install --id GitHub.cli' }
$ghPath = if ($gh.PSObject.Properties['Source'] -and -not [string]::IsNullOrWhiteSpace($gh.Source)) { $gh.Source } else { $gh.FullName }

& $ghPath auth status
if ($LASTEXITCODE -ne 0) { throw 'Sign in first with: gh auth login --web' }

$packageParameters = @{ GitHubRepository = $Repository; Version = $Version; Zip = $true }
if (-not [string]::IsNullOrWhiteSpace($RuntimeSource)) { $packageParameters.RuntimeSource = $RuntimeSource }
& (Join-Path $projectRoot 'package.ps1') @packageParameters
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Push-Location $projectRoot
try {
    if (-not (Test-Path -LiteralPath (Join-Path $projectRoot '.git'))) {
        git init -b main
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    $githubUser = (& $ghPath api user '--jq' '.login').Trim()
    $githubUserId = (& $ghPath api user '--jq' '.id').Trim()
    if ([string]::IsNullOrWhiteSpace((git config --local user.name))) { git config --local user.name $githubUser }
    if ([string]::IsNullOrWhiteSpace((git config --local user.email))) { git config --local user.email "$githubUserId+$githubUser@users.noreply.github.com" }
    git add --all
    git diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        git commit -m "Release Codex Legion v$Version"
        if ($LASTEXITCODE -ne 0) { throw 'Git needs your user.name and user.email before it can create the release commit.' }
    }

    & $ghPath repo view $Repository '--json' nameWithOwner 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        & $ghPath repo create $Repository "--$Visibility" '--source' $projectRoot '--remote' origin
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    } elseif (-not (git remote 2>$null | Select-String -SimpleMatch 'origin')) {
        git remote add origin "https://github.com/$Repository.git"
    }

    git push -u origin main
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $tag = 'v' + $Version.TrimStart('v')
    $channel = Get-Content -LiteralPath 'dist\CodexLegion\update-channel.json' -Raw | ConvertFrom-Json
    $runtimeTag = [string]$channel.runtimeReleaseTag
    & $ghPath release create $runtimeTag `
        'dist\CodexLegionRuntime-win-x64.zip' `
        'dist\CodexLegionRuntime-win-x64.zip.sha256' `
        '--prerelease' `
        '--title' 'Codex Legion runtime support' `
        '--notes' 'Support package used automatically by the Codex Legion web installer.'
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $assets = @(
        'dist\CodexLegionSetup.exe',
        'dist\CodexLegionSetup-offline.exe',
        'README.md'
    )
    & $ghPath release create $tag @assets '--title' "Codex Legion $tag" '--notes' "Codex Legion can talk now. Tap the mic for a hands-free Realtime conversation, or keep happily typing. Use CodexLegionSetup.exe for the tiny web installer or CodexLegionSetup-offline.exe for the everything-in-the-box edition."
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    Write-Host "Published https://github.com/$Repository/releases/tag/$tag"
} finally {
    Pop-Location
}
