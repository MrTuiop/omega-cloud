# ==============================================================================
# 🌌 Omega Cloud — Master Server Automatic Deployment Script (PowerShell)
# ==============================================================================
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "      🌌 OMEGA CLOUD — MASTER SERVER DEPLOYMENT WIZARD            " -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

# 1. Проверка Docker и Docker Compose
try {
    $dockerVersion = docker --version
    Write-Host "[✓] Docker обнаружен: $dockerVersion" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] Docker не установлен или не запущен! Запустите Docker Desktop." -ForegroundColor Red
    exit 1
}

try {
    $composeVersion = docker compose version
    Write-Host "[✓] Docker Compose обнаружен: $composeVersion" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] Docker Compose не найден!" -ForegroundColor Red
    exit 1
}

# 2. Подготовка .env файла
$rootDir = (Get-Item $PSScriptRoot).Parent.FullName
Set-Location $rootDir

$envPath = Join-Path $rootDir ".env"
$envExamplePath = Join-Path $rootDir ".env.example"

if (-not (Test-Path $envPath)) {
    Write-Host "[!] Файл .env не найден. Создаем из шаблона .env.example..." -ForegroundColor Yellow
    if (-not (Test-Path $envExamplePath)) {
        Write-Host "[ERROR] Шаблон .env.example не найден!" -ForegroundColor Red
        exit 1
    }
    
    $content = Get-Content $envExamplePath -Raw

    # Генерация криптографически стойких секретов
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    
    $jwtBytes = New-Object byte[] 36
    $rng.GetBytes($jwtBytes)
    $genJwtSecret = [Convert]::ToBase64String($jwtBytes) -replace '[^a-zA-Z0-9]', 'x'

    $pgBytes = New-Object byte[] 18
    $rng.GetBytes($pgBytes)
    $genPgPass = [Convert]::ToBase64String($pgBytes) -replace '[^a-zA-Z0-9]', 'y'

    $content = $content -replace "generate_super_strong_secret_password_here", $genPgPass
    $content = $content -replace "super_long_and_cryptographically_secure_jwt_secret_key_at_least_32_chars", $genJwtSecret

    # Интерактивный запрос домена
    Write-Host ""
    $userDomain = Read-Host "Введите домен сервера (по умолчанию 'localhost' для локального теста / IP)"
    if (-not [string]::IsNullOrWhiteSpace($userDomain) -and $userDomain -ne "localhost") {
        $userEmail = Read-Host "Введите email для SSL сертификата Let's Encrypt"
        $content = $content -replace "DOMAIN=localhost", "DOMAIN=$userDomain"
        $content = $content -replace "CADDY_TLS=internal", "CADDY_TLS=$userEmail"
        Write-Host "[✓] Настроен боевой домен: $userDomain (TLS: $userEmail)" -ForegroundColor Green
    } else {
        Write-Host "[i] Используется локальный режим (localhost / internal self-signed TLS)." -ForegroundColor Yellow
    }

    Set-Content -Path $envPath -Value $content -Encoding UTF8
    Write-Host "[✓] Файл .env успешно создан со сгенерированными секретами." -ForegroundColor Green
} else {
    Write-Host "[✓] Конфигурация .env уже существует." -ForegroundColor Green
}

# 3. Создание каталогов для постоянных данных хоста
$dataDirs = @("./data/postgres", "./data/storage", "./data/caddy", "./data/caddy_config")
foreach ($dir in $dataDirs) {
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

# 4. Сборка и запуск контейнеров
Write-Host ""
Write-Host "[*] Запуск сборки и развертывания контейнеров Omega Cloud Master..." -ForegroundColor Cyan
docker compose up -d --build

# 5. Ожидание готовности
Write-Host ""
Write-Host "[*] Ожидание запуска сервисов и миграций базы данных..." -ForegroundColor Cyan

$maxRetries = 30
$count = 0
$healthy = $false

while ($count -lt $maxRetries) {
    try {
        $healthOut = docker compose exec -T backend curl -s -f http://localhost:5000/health 2>$null
        if ($LASTEXITCODE -eq 0) {
            $healthy = $true
            break
        }
    } catch { }

    $count++
    Write-Host -NoNewline "."
    Start-Sleep -Seconds 2
}
Write-Host ""

if ($healthy) {
    Write-Host "==================================================================" -ForegroundColor Green
    Write-Host "       🚀 OMEGA CLOUD MASTER УСПЕШНО ЗАПУЩЕН И ГОТОВ К РАБОТЕ!     " -ForegroundColor Green
    Write-Host "==================================================================" -ForegroundColor Green

    $envContent = Get-Content $envPath
    $domainLine = $envContent | Where-Object { $_ -match "^DOMAIN=(.*)$" }
    $adminUserLine = $envContent | Where-Object { $_ -match "^ADMIN_USERNAME=(.*)$" }
    $adminPassLine = $envContent | Where-Object { $_ -match "^ADMIN_PASSWORD=(.*)$" }

    $domain = if ($domainLine) { $domainLine.Split('=')[1] } else { "localhost" }
    $adminUser = if ($adminUserLine) { $adminUserLine.Split('=')[1] } else { "admin" }
    $adminPass = if ($adminPassLine) { $adminPassLine.Split('=')[1] } else { "AdminPassword123!" }

    $accessUrl = if ($domain -eq "localhost") { "https://localhost" } else { "https://$domain" }

    Write-Host " Веб-интерфейс:     $accessUrl" -ForegroundColor Cyan
    Write-Host " Логин админа:      $adminUser" -ForegroundColor Green
    Write-Host " Пароль админа:     $adminPass" -ForegroundColor Green
    Write-Host ""
    Write-Host "ВАЖНО: При первом входе смените пароль администратора в профиле!" -ForegroundColor Yellow
    Write-Host "Для подключения воркер-нод перейдите в панель: $accessUrl/admin/nodes" -ForegroundColor Cyan
    Write-Host "==================================================================" -ForegroundColor Cyan
} else {
    Write-Host "[WARNING] Сервисы запущены, но проверка healthcheck бэкенда заняла больше времени, чем ожидалось." -ForegroundColor Yellow
    Write-Host "Проверьте логи командой: docker compose logs -f backend"
}
