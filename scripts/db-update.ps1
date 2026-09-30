# Applies EF Core migrations to the database in .env (ConnectionStrings__Default).
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if (-not (Test-Path .env)) { throw '.env not found. Run ./scripts/setup.ps1 first.' }
Get-Content .env | Where-Object { $_ -match '^\s*[^#\s][^=]*=' } | ForEach-Object {
    $name, $value = $_ -split '=', 2
    [Environment]::SetEnvironmentVariable($name.Trim(), $value.Trim())
}
dotnet tool restore
dotnet ef database update --project src/Portal.Infrastructure --startup-project src/Portal.Api
