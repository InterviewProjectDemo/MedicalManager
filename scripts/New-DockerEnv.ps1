# Creates a local .env with a random Postgres password and AES-256 key.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$envPath = Join-Path $root ".env"

if (Test-Path $envPath) {
    Write-Host ".env already exists at $envPath"
    exit 0
}

$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$keyBytes = New-Object byte[] 32
$rng.GetBytes($keyBytes)
$rng.Dispose()
$phiKey = [Convert]::ToBase64String($keyBytes)

$passwordChars = (48..57) + (65..90) + (97..122)
$password = -join ($passwordChars | Get-Random -Count 28 | ForEach-Object { [char]$_ })

$content = @"
POSTGRES_USER=medical
POSTGRES_PASSWORD=$password
POSTGRES_DB=medicalmanager
MEDICALMANAGER_PHI_KEY=$phiKey
"@
$utf8 = New-Object System.Text.UTF8Encoding $false
[System.IO.File]::WriteAllText($envPath, $content, $utf8)

Write-Host "Created $envPath with a random database password and PHI encryption key."
Write-Host "Keep this file private. Do not commit it."
