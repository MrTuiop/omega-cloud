#!/usr/bin/env bash
# ==============================================================================
# 🌌 Omega Cloud — Storage Node Worker Installation Script
# ==============================================================================
set -e

CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m'

echo -e "${CYAN}"
echo "=================================================================="
echo "    🌌 OMEGA CLOUD — STORAGE NODE INSTALLATION & REGISTRATION     "
echo "=================================================================="
echo -e "${NC}"

if ! command -v docker &> /dev/null; then
    echo -e "${RED}[ERROR] Docker не установлен! Установите Docker перед запуском ноды.${NC}"
    exit 1
fi

if ! docker compose version &> /dev/null; then
    echo -e "${RED}[ERROR] Docker Compose (v2) не найден!${NC}"
    exit 1
fi

DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$DIR"

if [ ! -f ".env" ]; then
    echo -e "${YELLOW}[!] Файл .env не найден. Настройка подключения к Master-серверу...${NC}"
    cp .env.example .env

    # 1. URL Мастера
    echo ""
    read -r -p "Введите адрес Master-сервера (например, https://cloud.example.com или http://192.168.1.100:5000): " INPUT_MASTER_URL
    while [ -z "$INPUT_MASTER_URL" ]; do
        echo -e "${RED}Адрес Master-сервера обязателен!${NC}"
        read -r -p "Введите адрес Master-сервера: " INPUT_MASTER_URL
    done

    # 2. Registration Token
    echo ""
    echo -e "${CYAN}Токен регистрации генерируется в панели Master-сервера: Веб-интерфейс -> Управление нодами -> Generate Node Token.${NC}"
    read -r -p "Введите Registration Token: " INPUT_REG_KEY
    while [ -z "$INPUT_REG_KEY" ]; do
        echo -e "${RED}Registration Token обязателен для первого сопряжения ноды!${NC}"
        read -r -p "Введите Registration Token: " INPUT_REG_KEY
    done

    # 3. Имя ноды
    DEFAULT_NAME="$(hostname 2>/dev/null || echo 'omega-worker-1')"
    read -r -p "Введите имя этой ноды [по умолчанию: $DEFAULT_NAME]: " INPUT_NODE_NAME
    [ -z "$INPUT_NODE_NAME" ] && INPUT_NODE_NAME="$DEFAULT_NAME"

    # 4. Порт ноды
    read -r -p "Порт для запуска демона на хосте [по умолчанию: 5001]: " INPUT_PORT
    [ -z "$INPUT_PORT" ] && INPUT_PORT="5001"

    # 5. Прямой URL или NAT
    echo ""
    echo "Есть ли у этой ноды внешний белый IP / домен для прямых загрузок в обход мастера?"
    read -r -p "Введите публичный URL (например https://nas.example.com:5001) или нажмите Enter если нода за NAT: " INPUT_PUBLIC_URL

    # Запись в .env
    sed -i "s|MASTER_BASE_URL=.*|MASTER_BASE_URL=${INPUT_MASTER_URL}|g" .env
    sed -i "s|NODE_REGISTRATION_KEY=.*|NODE_REGISTRATION_KEY=${INPUT_REG_KEY}|g" .env
    sed -i "s|NODE_NAME=.*|NODE_NAME=${INPUT_NODE_NAME}|g" .env
    sed -i "s|STORAGE_NODE_PORT=.*|STORAGE_NODE_PORT=${INPUT_PORT}|g" .env

    if [ -n "$INPUT_PUBLIC_URL" ]; then
        sed -i "s|NODE_BASE_URL=.*|NODE_BASE_URL=${INPUT_PUBLIC_URL}|g" .env
        sed -i "s|# NODE_IS_BEHIND_NAT=.*|NODE_IS_BEHIND_NAT=false|g" .env
        echo -e "${GREEN}[✓] Режим Direct Pre-signed Transfers активирован (${INPUT_PUBLIC_URL}).${NC}"
    else
        sed -i "s|NODE_BASE_URL=.*|NODE_BASE_URL=|g" .env
        sed -i "s|# NODE_IS_BEHIND_NAT=.*|NODE_IS_BEHIND_NAT=true|g" .env
        echo -e "${YELLOW}[i] Нода будет работать в режиме NAT Proxy Fallback (потоки через Master).${NC}"
    fi

    echo -e "${GREEN}[✓] Файл конфигурации .env сформирован.${NC}"
else
    echo -e "${GREEN}[✓] Существующий .env обнаружен.${NC}"
fi

mkdir -p ./data/storage

echo ""
echo -e "${CYAN}[*] Сборка и запуск Storage Node демона...${NC}"
docker compose up -d --build

echo ""
echo -e "${CYAN}[*] Ожидание готовности ноды...${NC}"
sleep 4

if curl -s -f "http://localhost:${INPUT_PORT:-5001}/health" > /dev/null 2>&1; then
    echo -e "${GREEN}=================================================================="
    echo "        🎉 STORAGE NODE УСПЕШНО ЗАПУЩЕНА И ПОДКЛЮЧЕНА!           "
    echo "=================================================================="
    echo -e "${NC}"
    echo "Нода отправляет Heartbeat на Master сервер каждые 15 секунд."
    echo "Проверьте статус ноды в веб-интерфейсе в разделе 'Storage Nodes & Fleet'."
    echo "Логи ноды: docker compose logs -f storage-node"
    echo "=================================================================="
else
    echo -e "${YELLOW}[!] Контейнер запущен, проверяем логи...${NC}"
    docker compose logs --tail 20 storage-node
fi
