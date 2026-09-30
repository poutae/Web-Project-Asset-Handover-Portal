# Local setup: creates .env from .env.example and installs dependencies.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if (-not (Test-Path .env)) {
    Copy-Item .env.example .env
    Write-Host 'Created .env - edit ConnectionStrings__Default for your local SQL Server.'
}
dotnet restore
dotnet tool restore
npm install
