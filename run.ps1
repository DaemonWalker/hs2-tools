$ErrorActionPreference = "Stop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet was not found. Install the .NET 8 SDK first."
    exit 1
}

Set-Location -LiteralPath $PSScriptRoot
dotnet run --project "HS2Tools/HS2Tools.csproj"
