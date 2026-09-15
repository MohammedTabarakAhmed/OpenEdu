# TST-04: the full automated suite (server unit + integration, client unit) by a single command.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

dotnet test
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

npm --prefix client test -- --watch=false
exit $LASTEXITCODE
