param(
    [string]$ProjectPath = "..\DBServersManager\DBServersManager.csproj",
    [string]$InnoScriptPath = ".\DBServersManager.iss"
)

$ErrorActionPreference = "Stop"

Write-Host "Publishing application..." -ForegroundColor Cyan
dotnet publish $ProjectPath -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true


# Force ISCC.exe path for reliability
$isccPath = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $isccPath)) {
    throw "ISCC.exe not found at $isccPath. Please check your Inno Setup installation."
}

Write-Host "Building installer with Inno Setup..." -ForegroundColor Cyan
& $isccPath $InnoScriptPath

Write-Host "Done. Installer generated in installer folder." -ForegroundColor Green
