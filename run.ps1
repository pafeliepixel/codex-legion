[CmdletBinding()]
param()

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$app = Join-Path $projectRoot 'build\Release\CodexLegion.exe'

if (-not (Test-Path -LiteralPath $app)) {
    & (Join-Path $projectRoot 'build.ps1')
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Start-Process -FilePath $app -WorkingDirectory $projectRoot
