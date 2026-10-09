# ==============================================================================
# 🌌 Omega Cloud — Storage Node Worker Installation Script (PowerShell)
# ==============================================================================
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "    🌌 OMEGA CLOUD — STORAGE NODE INSTALLATION & REGISTRATION     " -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

# 1. Проверка Docker
try {
    $dockerVer = docker --version
    Write-Host "[✓] Docker обнаружен: $dockerVer" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] Docker не установлен или не запущен!" -ForegroundColor Red
    exit 1
}

$dir = $PSScriptRoot
Set-Location $dir

$envFile = Join-Path $dir ".env"
$envExample = Join-Path $dir ".env.example"

if (-not (Test-Path $envFile)) {
    Write-Host "[!] Файл .env не найден. Настройка подключения к Master-серверу..." -ForegroundColor Yellow
    $content = Get-Content $envExample -Raw

    Write-Host ""
    $masterUrl = Read-Host "Введите адрес Master-сервера (например, https://cloud.example.com или http://192.168.1.100:5000)"
    while ([string]::IsNullOrWhiteSpace($masterUrl)) {
        Write-Host "Адрес Master-сервера обязателен!" -ForegroundColor Red
        $masterUrl = Read-Host "Введите адрес Master-сервера"
    }

    Write-Host ""
    Write-Host "Токен регистрации генерируется в панели Master-сервера: Веб-интерфейс -> Управление нодами -> Generate Node Token." -ForegroundColor Cyan
    $regKey = Read-Host "Введите Registration Token"
    while ([string]::IsNullOrWhiteSpace($regKey)) {
        Write-Host "Registration Token обязателен для первого сопряжения ноды!" -ForegroundColor Red
        $regKey = Read-Host "Введите Registration Token"
    }

    $defaultNodeName = $env:COMPUTERNAME
    if ([string]::IsNullOrWhiteSpace($defaultNodeName)) { $defaultNodeName = "omega-worker-win" }
    $nodeName = Read-Host "Введите имя этой ноды [по умолчанию: $defaultNodeName]"
    if ([string]::IsNullOrWhiteSpace($nodeName)) { $nodeName = $defaultNodeName }

    $nodePort = Read-Host "Порт для запуска демона на хосте [по умолчанию: 5001]"
    if ([string]::IsNullOrWhiteSpace($nodePort)) { $nodePort = "5001" }

    Write-Host ""
    $publicUrl = Read-Host "Введите публичный URL (например https://nas.example.com:5001) или нажмите Enter если нода за NAT"

    $content = $content -replace "MASTER_BASE_URL=.*", "MASTER_BASE_URL=$masterUrl"
    $content = $content -replace "NODE_REGISTRATION_KEY=.*", "NODE_REGISTRATION_KEY=$regKey"
    $content = $content -replace "NODE_NAME=.*", "NODE_NAME=$nodeName"
    $content = $content -replace "STORAGE_NODE_PORT=.*", "STORAGE_NODE_PORT=$nodePort"

    if (-not [string]::IsNullOrWhiteSpace($publicUrl)) {
        $content = $content -replace "NODE_BASE_URL=.*", "NODE_BASE_URL=$publicUrl"
        $content = $content -replace "# NODE_IS_BEHIND_NAT=.*", "NODE_IS_BEHIND_NAT=false"
        Write-Host "[✓] Режим Direct Pre-signed Transfers ($publicUrl)." -ForegroundColor Green
    } else {
        $content = $content -replace "NODE_BASE_URL=.*", "NODE_BASE_URL="
        $content = $content -replace "# NODE_IS_BEHIND_NAT=.*", "NODE_IS_BEHIND_NAT=true"
        Write-Host "[i] Режим NAT Proxy Fallback (потоки через Master)." -ForegroundColor Yellow
    }

    Set-Content -Path $envFile -Value $content -Encoding UTF8
    Write-Host "[✓] Файл конфигурации .env успешно сохранен." -ForegroundColor Green
} else {
    Write-Host "[✓] Существующий .env обнаружен." -ForegroundColor Green
}

if (-not (Test-Path "./data/storage")) {
    New-Item -ItemType Directory -Path "./data/storage" -Force | Out-Null
}

Write-Host ""
Write-Host "[*] Сборка и запуск Storage Node демона..." -ForegroundColor Cyan
docker compose up -d --build

Write-Host ""
Write-Host "[*] Ожидание готовности ноды..." -ForegroundColor Cyan
Start-Sleep -Seconds 4

Write-Host "==================================================================" -ForegroundColor Green
Write-Host "        🎉 STORAGE NODE ЗАПУЩЕНА!                                " -ForegroundColor Green
Write-Host "==================================================================" -ForegroundColor Green
Write-Host "Нода отправляет Heartbeat на Master сервер каждые 15 секунд."
Write-Host "Проверьте статус ноды в веб-интерфейсе в разделе 'Storage Nodes & Fleet'."
Write-Host "Логи ноды: docker compose logs -f storage-node"
Write-Host "==================================================================" -ForegroundColor Cyan
