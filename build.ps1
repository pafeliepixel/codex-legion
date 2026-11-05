[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$gacRoot = Join-Path $env:WINDIR 'Microsoft.NET\assembly'
$compiler = Join-Path $frameworkRoot 'csc.exe'
$appIcon = Join-Path $projectRoot 'assets\CodexNativeLite.ico'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The built-in .NET Framework C# compiler was not found.'
}
if (-not (Test-Path -LiteralPath $appIcon)) {
    throw 'The app icon is missing. Run generate-icon.ps1 first.'
}

$outputDirectory = Join-Path $projectRoot "build\$Configuration"
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$compilerArguments = @(
    '/nologo'
    '/target:winexe'
    '/platform:x64'
    '/utf8output'
    "/win32icon:$appIcon"
    "/out:$outputDirectory\CodexLegion.exe"
    "/reference:$frameworkRoot\System.dll"
    "/reference:$frameworkRoot\System.Core.dll"
    "/reference:$frameworkRoot\System.Drawing.dll"
    "/reference:$frameworkRoot\System.Management.dll"
    "/reference:$frameworkRoot\System.IO.Compression.dll"
    "/reference:$frameworkRoot\System.IO.Compression.FileSystem.dll"
    "/reference:$frameworkRoot\System.Security.dll"
    "/reference:$frameworkRoot\System.Web.Extensions.dll"
    "/reference:$frameworkRoot\System.Net.WebSockets.dll"
    "/reference:$frameworkRoot\System.Net.WebSockets.Client.dll"
    "/reference:$frameworkRoot\System.Windows.Forms.dll"
    "/reference:$gacRoot\GAC_MSIL\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll"
    "/reference:$gacRoot\GAC_64\PresentationCore\v4.0_4.0.0.0__31bf3856ad364e35\PresentationCore.dll"
    "/reference:$gacRoot\GAC_MSIL\PresentationFramework\v4.0_4.0.0.0__31bf3856ad364e35\PresentationFramework.dll"
    "/reference:$frameworkRoot\System.Xaml.dll"
)

if ($Configuration -eq 'Debug') {
    $compilerArguments += '/debug:full', '/optimize-'
} else {
    $compilerArguments += '/debug-', '/optimize+'
}

$compilerArguments += @(
    (Join-Path $projectRoot 'App.cs')
    (Join-Path $projectRoot 'CodexAppServerClient.cs')
    (Join-Path $projectRoot 'CodexAppServerFeatures.cs')
    (Join-Path $projectRoot 'ProtocolModels.cs')
    (Join-Path $projectRoot 'AppSettingsStore.cs')
    (Join-Path $projectRoot 'UpdateService.cs')
    (Join-Path $projectRoot 'ComponentUpdates.cs')
    (Join-Path $projectRoot 'GitService.cs')
    (Join-Path $projectRoot 'MainWindow.cs')
    (Join-Path $projectRoot 'MainWindowFeatures.cs')
    (Join-Path $projectRoot 'MainWindowParallel.cs')
    (Join-Path $projectRoot 'ProfileSecurity.cs')
    (Join-Path $projectRoot 'VoiceCredentialStore.cs')
    (Join-Path $projectRoot 'WinMmAudio.cs')
    (Join-Path $projectRoot 'RealtimeVoiceSession.cs')
    (Join-Path $projectRoot 'MainWindowVoice.cs')
    (Join-Path $projectRoot 'StarterWorkspace.cs')
    (Join-Path $projectRoot 'BrowserToolContract.cs')
    (Join-Path $projectRoot 'ManagedBrowser.cs')
    (Join-Path $projectRoot 'MainWindowBrowser.cs')
    (Join-Path $projectRoot 'LocalModelCatalog.cs')
    (Join-Path $projectRoot 'LocalModelStorage.cs')
    (Join-Path $projectRoot 'LocalDownloadProgress.cs')
    (Join-Path $projectRoot 'EmbeddedStrataManager.cs')
    (Join-Path $projectRoot 'MainWindowLocalAi.cs')
    (Join-Path $projectRoot 'Properties\AssemblyInfo.cs')
)

& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$output = Join-Path $projectRoot "build\$Configuration\CodexLegion.exe"
Write-Host "Built $output"
