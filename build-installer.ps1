[CmdletBinding()]
param([switch]$PreserveUninstaller)

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
$distRoot = Join-Path $projectRoot 'dist\CodexLegion'
$installerBuild = Join-Path $projectRoot 'build\Installer'
$stub = Join-Path $installerBuild 'CodexLegionSetup.stub.exe'
$setup = Join-Path $projectRoot 'dist\CodexLegionSetup.exe'
$offlineSetup = Join-Path $projectRoot 'dist\CodexLegionSetup-offline.exe'
$runtimeArchive = Join-Path $projectRoot 'dist\CodexLegionRuntime-win-x64.zip'
$icon = Join-Path $projectRoot 'assets\CodexNativeLite.ico'

if (-not (Test-Path -LiteralPath (Join-Path $distRoot 'CodexLegion.exe'))) {
    throw 'Package Codex Legion before building the installer.'
}
New-Item -ItemType Directory -Force -Path $installerBuild | Out-Null

& $compiler /nologo /target:winexe /platform:x64 /optimize+ "/win32icon:$icon" "/out:$stub" `
    "/reference:$frameworkRoot\System.dll" `
    "/reference:$frameworkRoot\System.Core.dll" `
    "/reference:$frameworkRoot\System.Drawing.dll" `
    "/reference:$frameworkRoot\System.IO.Compression.dll" `
    "/reference:$frameworkRoot\System.IO.Compression.FileSystem.dll" `
    "/reference:$frameworkRoot\System.Security.dll" `
    "/reference:$frameworkRoot\System.Web.Extensions.dll" `
    "/reference:$frameworkRoot\System.Windows.Forms.dll" `
    (Join-Path $projectRoot 'installer\InstallerApp.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$uninstaller = Join-Path $distRoot 'Uninstall Codex Legion.exe'
if (-not $PreserveUninstaller -or -not (Test-Path -LiteralPath $uninstaller)) {
    Copy-Item -LiteralPath $stub -Destination $uninstaller -Force
}

$files = @(Get-ChildItem -LiteralPath $distRoot -Recurse -File | Sort-Object FullName)

function Write-Setup([string]$Destination, [object[]]$PayloadFiles) {
    Copy-Item -LiteralPath $stub -Destination $Destination -Force
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::Append, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $writer = New-Object IO.BinaryWriter($stream, [Text.Encoding]::UTF8)
    try {
        $payloadOffset = $stream.Position
        $writer.Write([int]1)
        $writer.Write([int]$PayloadFiles.Count)
        foreach ($file in $PayloadFiles) {
            $relative = if ($file.FullName.StartsWith($distRoot, [StringComparison]::OrdinalIgnoreCase)) {
                $file.FullName.Substring($distRoot.Length).TrimStart('\').Replace('\', '/')
            } else {
                $file.Name
            }
            $writer.Write([string]$relative)
            $writer.Write([long]$file.Length)
            $hash = [Security.Cryptography.SHA256]::Create()
            try {
                $input = [IO.File]::OpenRead($file.FullName)
                try { $digest = $hash.ComputeHash($input) } finally { $input.Dispose() }
            } finally { $hash.Dispose() }
            $writer.Write([byte[]]$digest)
            $input = [IO.File]::OpenRead($file.FullName)
            try { $input.CopyTo($stream) } finally { $input.Dispose() }
        }
        $writer.Write([long]$payloadOffset)
        $writer.Write([byte[]][Text.Encoding]::ASCII.GetBytes('CLGNPKGFORMAT001'))
    } finally {
        $writer.Dispose()
    }
    $hashText = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content -LiteralPath ($Destination + '.sha256') -Value ($hashText + '  ' + (Split-Path -Leaf $Destination)) -Encoding ASCII
}

$runtimeDirectory = Join-Path $distRoot 'runtime'
if (Test-Path -LiteralPath $runtimeArchive) { Remove-Item -LiteralPath $runtimeArchive -Force }
Compress-Archive -Path (Join-Path $runtimeDirectory '*') -DestinationPath $runtimeArchive -CompressionLevel Optimal
$runtimeHash = (Get-FileHash -LiteralPath $runtimeArchive -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath ($runtimeArchive + '.sha256') -Value ($runtimeHash + '  CodexLegionRuntime-win-x64.zip') -Encoding ASCII

$webFiles = @($files | Where-Object { $_.FullName -notlike ($runtimeDirectory + '\*') })
Write-Setup $setup $webFiles
$offlineFiles = @($webFiles + (Get-Item -LiteralPath $runtimeArchive))
Write-Setup $offlineSetup $offlineFiles
Write-Host "Built web installer $setup"
Write-Host "Built offline installer $offlineSetup"
Write-Host "Built runtime package $runtimeArchive"

