$ErrorActionPreference = "Stop"
$ProjectDir = "city-print-smart-routing.Server"
$OutputDir  = "publish-output"

Write-Host "=== Публикация city-print-smart-routing ===" -ForegroundColor Cyan

if (Test-Path $OutputDir) {
    Write-Host "Очистка $OutputDir..."
    Remove-Item -Recurse -Force $OutputDir
}

dotnet publish "$ProjectDir/city-print-smart-routing.Server.csproj" `
    -c Release `
    -o $OutputDir `
    --nologo

if ($LASTEXITCODE -ne 0) { exit 1 }

$exe = "$OutputDir\city-print-smart-routing.exe"
if (-not (Test-Path $exe)) {
    Write-Error "Exe не найден: $exe"
    exit 1
}

Write-Host ""
Write-Host "=== Готово ===" -ForegroundColor Green
Write-Host "Exe:  $exe"
Write-Host "Папка: $OutputDir"
Write-Host ""
Write-Host "Запуск напрямую:         .\$OutputDir\city-print-smart-routing.exe"
Write-Host "Установить как службу:   .\install-service.ps1"
