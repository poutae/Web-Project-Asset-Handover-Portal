# Fails if sensitive files are tracked by Git.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$bad = git ls-files | Where-Object { $_ -match '(^|/)\.env($|\.(?!example$))' -or $_ -match '\.(pfx|key|pem)$' }
if ($bad) { Write-Error "Sensitive files tracked: $bad"; exit 1 }
Write-Host 'No sensitive files tracked.'
