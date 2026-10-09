# ==============================================================================
# 🌌 Omega Cloud — Storage Node Bundle Generator (PowerShell)
# ==============================================================================
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$rootDir = (Get-Item $PSScriptRoot).Parent.FullName
$distDir = Join-Path $rootDir "dist"
$bundleDir = Join-Path $distDir "omega-storage-node"
$srcNodeDir = Join-Path $rootDir "backend\src\Omega.StorageNode"

Write-Host "==================================================================" -ForegroundColor Cyan
Write-Host "    📦 OMEGA CLOUD — СБОРКА АВТОНОМНОГО ПАКЕТА STORAGE NODE       " -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Cyan

if (Test-Path $bundleDir) { Remove-Item -Recurse -Force $bundleDir }
$tarPath = Join-Path $distDir "omega-storage-node.tar.gz"
$zipPath = Join-Path $distDir "omega-storage-node.zip"
if (Test-Path $tarPath) { Remove-Item -Force $tarPath }
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }

New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null

Write-Host "[*] Копирование файлов Storage Node..." -ForegroundColor Cyan

# Копируем всё чистое
Get-ChildItem -Path $srcNodeDir | Where-Object { 
    $_.Name -notin @("bin", "obj", "data", ".vs") 
} | ForEach-Object {
    Copy-Item -Path $_.FullName -Destination $bundleDir -Recurse -Force
}

# Архивируем в zip
Write-Host "[*] Создание архива .zip..." -ForegroundColor Cyan
Compress-Archive -Path "$bundleDir\*" -DestinationPath $zipPath -Force

Write-Host "==================================================================" -ForegroundColor Green
Write-Host " [✓] ПАКЕТ УСПЕШНО СОБРАН!" -ForegroundColor Green
Write-Host " Папка пакета:  $bundleDir" -ForegroundColor Cyan
Write-Host " ZIP архив:     $zipPath" -ForegroundColor Cyan
Write-Host "==================================================================" -ForegroundColor Green
Write-Host "Как задеплоить на удаленный сервер или NAS:"
Write-Host ""
Write-Host " 1. Скопируйте папку 'omega-storage-node' или zip-архив на целевую машину."
Write-Host " 2. Запустите в ней: ./install-node.sh (на Linux) или .\install-node.ps1 (на Windows)"
Write-Host " 3. Введите токен регистрации из веб-панели Master-сервера."
Write-Host "==================================================================" -ForegroundColor Cyan
