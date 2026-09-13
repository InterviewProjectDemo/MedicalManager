$ErrorActionPreference = "Stop"
$fly = Join-Path $env:LOCALAPPDATA "fly\flyctl.exe"
if (-not (Test-Path $fly)) {
    throw "flyctl not found at $fly. Install from https://fly.io/docs/flyctl/install/"
}

Set-Location (Split-Path $PSScriptRoot -Parent)

& $fly auth whoami
if ($LASTEXITCODE -ne 0) {
    throw "Run this in a normal terminal first: `"$fly`" auth login"
}

$app = "medicalmgr-demo"
& $fly apps list
$exists = (& $fly status -a $app 2>$null)
if ($LASTEXITCODE -ne 0) {
    & $fly apps create $app --org personal
}

$vols = & $fly volumes list -a $app
if ($vols -notmatch "medicalmgr_data") {
    & $fly volumes create medicalmgr_data -a $app -r iad -s 1
}

& $fly deploy -a $app --remote-only
& $fly certs add medicalmgr.com -a $app
& $fly certs add www.medicalmgr.com -a $app
& $fly ips list -a $app
& $fly certs show www.medicalmgr.com -a $app
Write-Host "Point GoDaddy DNS A/AAAA (or ALIAS) for @ and www to the IPs above, then wait for https://www.medicalmgr.com"
