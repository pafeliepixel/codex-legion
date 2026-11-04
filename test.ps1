[CmdletBinding()]
param()

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$frameworkRoot = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $frameworkRoot 'csc.exe'
$outputDirectory = Join-Path $projectRoot 'build\Test'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& $compiler /nologo /target:exe /platform:x64 "/out:$outputDirectory\ProfileSecuritySmoke.exe" `
    "/reference:$frameworkRoot\System.dll" `
    "/reference:$frameworkRoot\System.Security.dll" `
    "/reference:$frameworkRoot\System.Web.Extensions.dll" `
    (Join-Path $projectRoot 'ProfileSecurity.cs') `
    (Join-Path $projectRoot 'tests\ProfileSecuritySmoke.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $outputDirectory 'ProfileSecuritySmoke.exe')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $compiler /nologo /target:exe /platform:x64 "/out:$outputDirectory\VoiceCredentialStoreSmoke.exe" `
    "/reference:$frameworkRoot\System.dll" `
    "/reference:$frameworkRoot\System.Security.dll" `
    (Join-Path $projectRoot 'VoiceCredentialStore.cs') `
    (Join-Path $projectRoot 'tests\VoiceCredentialStoreSmoke.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $outputDirectory 'VoiceCredentialStoreSmoke.exe')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $compiler /nologo /target:exe /platform:x64 "/out:$outputDirectory\StarterWorkspaceSmoke.exe" `
    "/reference:$frameworkRoot\System.dll" `
    (Join-Path $projectRoot 'StarterWorkspace.cs') `
    (Join-Path $projectRoot 'tests\StarterWorkspaceSmoke.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $outputDirectory 'StarterWorkspaceSmoke.exe')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $compiler /nologo /target:exe /platform:x64 "/out:$outputDirectory\BrowserToolSmoke.exe" `
    "/reference:$frameworkRoot\System.dll" `
    "/reference:$frameworkRoot\System.Core.dll" `
    "/reference:$frameworkRoot\System.Web.Extensions.dll" `
    "/reference:$frameworkRoot\System.Net.WebSockets.dll" `
    "/reference:$frameworkRoot\System.Net.WebSockets.Client.dll" `
    (Join-Path $projectRoot 'BrowserToolContract.cs') `
    (Join-Path $projectRoot 'ManagedBrowser.cs') `
    (Join-Path $projectRoot 'tests\BrowserToolSmoke.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $outputDirectory 'BrowserToolSmoke.exe')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& $compiler /nologo /target:exe /platform:x64 "/out:$outputDirectory\RuntimeSettingsSmoke.exe" `
    "/reference:$frameworkRoot\System.dll" `
    (Join-Path $projectRoot 'ProtocolModels.cs') `
    (Join-Path $projectRoot 'tests\RuntimeSettingsSmoke.cs')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $outputDirectory 'RuntimeSettingsSmoke.exe')
exit $LASTEXITCODE
