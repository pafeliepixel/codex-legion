[CmdletBinding()]
param(
    [string]$RuntimeSource,
    [string]$GitHubRepository,
    [string]$Version = '0.5.0',
    [switch]$Zip,
    [switch]$SkipInstaller
)

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$distRoot = Join-Path $projectRoot 'dist\CodexLegion'
$runtimeTarget = Join-Path $distRoot 'runtime'

if ([string]::IsNullOrWhiteSpace($RuntimeSource)) {
    if (-not [string]::IsNullOrWhiteSpace($env:CODEX_RUNTIME_SOURCE)) {
        $RuntimeSource = $env:CODEX_RUNTIME_SOURCE
    } else {
        $binRoot = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
        $candidate = Get-ChildItem -LiteralPath $binRoot -Filter 'codex.exe' -Recurse -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($null -ne $candidate) { $RuntimeSource = $candidate.DirectoryName }
    }
}

if ([string]::IsNullOrWhiteSpace($RuntimeSource) -or -not (Test-Path -LiteralPath (Join-Path $RuntimeSource 'codex.exe'))) {
    throw 'A Codex runtime directory is required. Pass -RuntimeSource or set CODEX_RUNTIME_SOURCE.'
}

& (Join-Path $projectRoot 'build.ps1') -Configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

New-Item -ItemType Directory -Force -Path $runtimeTarget | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'build\Release\CodexLegion.exe') -Destination $distRoot -Force
$packagedPdb = Join-Path $distRoot 'CodexLegion.pdb'
if (Test-Path -LiteralPath $packagedPdb) { Remove-Item -LiteralPath $packagedPdb -Force }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $distRoot -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'RUNTIME-NOTICE.txt') -Destination $distRoot -Force

$runtimeFiles = @(
    'codex.exe',
    'codex-code-mode-host.exe',
    'codex-command-runner.exe',
    'codex-windows-sandbox-setup.exe'
)
foreach ($name in $runtimeFiles) {
    $source = Join-Path $RuntimeSource $name
    if (Test-Path -LiteralPath $source) {
        $destination = Join-Path $runtimeTarget $name
        if (-not [String]::Equals(
            [IO.Path]::GetFullPath($source),
            [IO.Path]::GetFullPath($destination),
            [StringComparison]::OrdinalIgnoreCase)) {
            Copy-Item -LiteralPath $source -Destination $destination -Force
        }
    } elseif ($name -eq 'codex.exe') {
        throw "Missing required runtime file: $source"
    }
}

$runtimeVersion = (Get-FileHash -LiteralPath (Join-Path $runtimeTarget 'codex.exe') -Algorithm SHA256).Hash.Substring(0, 16).ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $runtimeTarget '.legion-runtime-version') -Value $runtimeVersion -Encoding ASCII

$manifest = foreach ($file in Get-ChildItem -LiteralPath $runtimeTarget -File) {
    $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
    [pscustomobject]@{ File = $file.Name; SHA256 = $hash.Hash; Bytes = $file.Length }
}
$manifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $runtimeTarget 'manifest.json') -Encoding UTF8

@{
    repository = if ([string]::IsNullOrWhiteSpace($GitHubRepository)) { '' } else { $GitHubRepository.Trim('/') }
    installerAsset = 'CodexLegionSetup.exe'
    releaseTag = 'v' + $Version.TrimStart('v')
    runtimeAsset = 'CodexLegionRuntime-win-x64.zip'
    runtimeVersion = $runtimeVersion
    runtimeReleaseTag = 'runtime-' + $runtimeVersion
} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $distRoot 'update-channel.json') -Encoding UTF8

if ($Zip) {
    $zipPath = Join-Path (Split-Path -Parent $distRoot) 'CodexLegion-portable.zip'
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Compress-Archive -LiteralPath $distRoot -DestinationPath $zipPath -CompressionLevel Optimal
    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($zipPath + '.sha256') -Value ($zipHash + '  CodexLegion-portable.zip') -Encoding ASCII
    Write-Host "Packaged $zipPath"
} else {
    Write-Host "Packaged $distRoot"
}

if (-not $SkipInstaller) {
    & (Join-Path $projectRoot 'build-installer.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

