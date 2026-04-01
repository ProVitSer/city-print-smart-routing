param(
    [switch]$Uninstall,
    [string]$Port = "7879"
)

$ErrorActionPreference = "Stop"

$ServiceName = "city-print-smart-routing"
$DisplayName = "City Print Smart Routing"
$Description = "Синхронизация контактов из 1С в телефонную книгу 3CX"
$InstallDir = "C:\Services\city-print-smart-routing"
$ExeName = "city-print-smart-routing.exe"
$ExePath = Join-Path $InstallDir $ExeName

# Проверка прав администратора
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error "Требуются права администратора. Запустите PowerShell от имени администратора."
    exit 1
}

# === Удаление ===
if ($Uninstall) {
    Write-Host "=== Удаление службы $ServiceName ===" -ForegroundColor Yellow

    $svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($svc) {
        if ($svc.Status -ne 'Stopped') {
            Write-Host "Остановка службы..."
            Stop-Service -Name $ServiceName -Force
            Start-Sleep -Seconds 3
        }
        sc.exe delete $ServiceName | Out-Null
        Write-Host "Служба удалена." -ForegroundColor Green
    } else {
        Write-Host "Служба не найдена." -ForegroundColor Yellow
    }
    exit 0
}

# === Установка ===
Write-Host "=== Установка службы $DisplayName ===" -ForegroundColor Cyan

# Проверить наличие артефактов
$sourceDir = "publish-output"
if (-not (Test-Path "$sourceDir/$ExeName")) {
    Write-Host "Артефакты не найдены в $sourceDir. Запустите .\publish.ps1 сначала." -ForegroundColor Red
    exit 1
}

# Создать директорию установки
if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir | Out-Null
    Write-Host "Создана директория: $InstallDir"
}

# Сохранить существующую БД
$dbFile = Join-Path $InstallDir "city-print-smart-routing.db"
$dbBackup = $null
if (Test-Path $dbFile) {
    $dbBackup = "$dbFile.backup"
    Copy-Item $dbFile $dbBackup
    Write-Host "База данных сохранена: $dbBackup" -ForegroundColor Yellow
}

# Остановить службу если запущена
$svc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne 'Stopped') {
    Write-Host "Остановка существующей службы..."
    Stop-Service -Name $ServiceName -Force
    Start-Sleep -Seconds 3
}

# Скопировать файлы
Write-Host "Копирование файлов в $InstallDir..."
Copy-Item -Path "$sourceDir\*" -Destination $InstallDir -Recurse -Force

# Восстановить БД
if ($dbBackup -and (Test-Path $dbBackup)) {
    Copy-Item $dbBackup $dbFile -Force
    Remove-Item $dbBackup -Force
    Write-Host "База данных восстановлена" -ForegroundColor Green
}

# Настройка порта в конфиге
$productionConfig = Join-Path $InstallDir "appsettings.Production.json"
if (Test-Path $productionConfig) {
    $config = Get-Content $productionConfig | ConvertFrom-Json
    $config.Urls = "http://0.0.0.0:$Port"
    $config | ConvertTo-Json -Depth 10 | Set-Content $productionConfig
}

# Установить или обновить службу
if ($svc) {
    Write-Host "Обновление существующей службы..."
    sc.exe config $ServiceName binPath= "`"$ExePath`"" | Out-Null
} else {
    Write-Host "Регистрация Windows Service..."
    New-Service `
        -Name $ServiceName `
        -DisplayName $DisplayName `
        -Description $Description `
        -BinaryPathName $ExePath `
        -StartupType Automatic | Out-Null
}

# Настройка автоперезапуска при сбое
sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

# Запуск службы
Write-Host "Запуск службы..."
Start-Service -Name $ServiceName
Start-Sleep -Seconds 3

$svc = Get-Service -Name $ServiceName
Write-Host ""
Write-Host "=== Установка завершена ===" -ForegroundColor Green
Write-Host "Служба: $ServiceName"
Write-Host "Статус: $($svc.Status)"
Write-Host "Директория: $InstallDir"
Write-Host "Порт: $Port"
Write-Host "API: http://localhost:$Port/swagger"
Write-Host ""
Write-Host "Управление службой:" -ForegroundColor Cyan
Write-Host "  Stop-Service $ServiceName"
Write-Host "  Start-Service $ServiceName"
Write-Host "  Restart-Service $ServiceName"
Write-Host "  Logs: $InstallDir\logs\"
