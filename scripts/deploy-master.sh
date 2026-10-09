#!/usr/bin/env bash
# ==============================================================================
# 🌌 Omega Cloud — Master Server Automatic Deployment Script
# ==============================================================================
set -e

CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${CYAN}"
echo "=================================================================="
echo "      🌌 OMEGA CLOUD — MASTER SERVER DEPLOYMENT WIZARD            "
echo "=================================================================="
echo -e "${NC}"

# 1. Проверка Docker и Docker Compose
if ! command -v docker &> /dev/null; then
    echo -e "${RED}[ERROR] Docker не установлен! Установите Docker перед запуском.${NC}"
    exit 1
fi

if ! docker compose version &> /dev/null; then
    echo -e "${RED}[ERROR] Docker Compose (v2) не найден! Убедитесь, что установлен docker compose-plugin.${NC}"
    exit 1
fi

echo -e "${GREEN}[✓] Docker и Docker Compose обнаружены.${NC}"

# 2. Подготовка .env файла
ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

if [ ! -f ".env" ]; then
    echo -e "${YELLOW}[!] Файл .env не найден. Создаем из шаблона .env.example...${NC}"
    if [ ! -f ".env.example" ]; then
        echo -e "${RED}[ERROR] Шаблон .env.example не найден!${NC}"
        exit 1
    fi
    cp .env.example .env

    # Генерация криптографически стойких секретов
    GEN_JWT_SECRET=$(openssl rand -base64 48 | tr -dc 'a-zA-Z0-9' | head -c 48 2>/dev/null || cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 48 | head -n 1)
    GEN_PG_PASS=$(openssl rand -base64 32 | tr -dc 'a-zA-Z0-9' | head -c 24 2>/dev/null || cat /dev/urandom | tr -dc 'a-zA-Z0-9' | fold -w 24 | head -n 1)

    # Замена дефолтных плейсхолдеров
    if [[ "$OSTYPE" == "darwin"* ]]; then
        sed -i '' "s/generate_super_strong_secret_password_here/${GEN_PG_PASS}/g" .env
        sed -i '' "s/super_long_and_cryptographically_secure_jwt_secret_key_at_least_32_chars/${GEN_JWT_SECRET}/g" .env
    else
        sed -i "s/generate_super_strong_secret_password_here/${GEN_PG_PASS}/g" .env
        sed -i "s/super_long_and_cryptographically_secure_jwt_secret_key_at_least_32_chars/${GEN_JWT_SECRET}/g" .env
    fi

    echo -e "${GREEN}[✓] Сгенерированы безопасные пароли для PostgreSQL и JWT Secret.${NC}"

    # Интерактивный запрос домена
    echo ""
    read -r -p "Введите домен сервера (по умолчанию 'localhost' для локального теста / IP): " USER_DOMAIN
    if [ -n "$USER_DOMAIN" ] && [ "$USER_DOMAIN" != "localhost" ]; then
        read -r -p "Введите email для SSL сертификата Let's Encrypt: " USER_EMAIL
        if [[ "$OSTYPE" == "darwin"* ]]; then
            sed -i '' "s/DOMAIN=localhost/DOMAIN=${USER_DOMAIN}/g" .env
            sed -i '' "s/CADDY_TLS=internal/CADDY_TLS=${USER_EMAIL}/g" .env
        else
            sed -i "s/DOMAIN=localhost/DOMAIN=${USER_DOMAIN}/g" .env
            sed -i "s/CADDY_TLS=internal/CADDY_TLS=${USER_EMAIL}/g" .env
        fi
        echo -e "${GREEN}[✓] Настроен боевой домен: ${USER_DOMAIN} (TLS: ${USER_EMAIL})${NC}"
    else
        echo -e "${YELLOW}[i] Используется локальный режим (localhost / internal self-signed TLS).${NC}"
    fi
else
    echo -e "${GREEN}[✓] Конфигурация .env уже существует.${NC}"
fi

# 3. Создание каталогов для постоянных данных хоста
mkdir -p ./data/postgres ./data/storage ./data/caddy ./data/caddy_config

# 4. Сборка и запуск контейнеров
echo ""
echo -e "${CYAN}[*] Запуск сборки и развертывания контейнеров Omega Cloud Master...${NC}"
docker compose pull --ignore-buildable || true
docker compose up -d --build

# 5. Ожидание готовности сервисов
echo ""
echo -e "${CYAN}[*] Ожидание запуска сервисов и миграций базы данных...${NC}"

MAX_RETRIES=30
COUNT=0
HEALTHY=false

while [ $COUNT -lt $MAX_RETRIES ]; do
    if docker compose ps | grep -q "omega-backend" && docker compose exec -T backend curl -s -f http://localhost:5000/health > /dev/null 2>&1; then
        HEALTHY=true
        break
    fi
    COUNT=$((COUNT + 1))
    echo -n "."
    sleep 2
done
echo ""

if [ "$HEALTHY" = true ]; then
    echo -e "${GREEN}=================================================================="
    echo "       🚀 OMEGA CLOUD MASTER УСПЕШНО ЗАПУЩЕН И ГОТОВ К РАБОТЕ!     "
    echo "=================================================================="
    echo -e "${NC}"
    
    # Чтение домена и учетных данных из .env
    DOMAIN_NAME=$(grep '^DOMAIN=' .env | cut -d '=' -f2)
    ADMIN_USER=$(grep '^ADMIN_USERNAME=' .env | cut -d '=' -f2)
    ADMIN_PASS=$(grep '^ADMIN_PASSWORD=' .env | cut -d '=' -f2)

    [ -z "$DOMAIN_NAME" ] && DOMAIN_NAME="localhost"
    [ -z "$ADMIN_USER" ] && ADMIN_USER="admin"
    [ -z "$ADMIN_PASS" ] && ADMIN_PASS="AdminPassword123!"

    if [ "$DOMAIN_NAME" = "localhost" ]; then
        ACCESS_URL="https://localhost"
    else
        ACCESS_URL="https://${DOMAIN_NAME}"
    fi

    echo -e " Веб-интерфейс:     ${CYAN}${ACCESS_URL}${NC}"
    echo -e " Логин админа:      ${GREEN}${ADMIN_USER}${NC}"
    echo -e " Пароль админа:     ${GREEN}${ADMIN_PASS}${NC}"
    echo ""
    echo -e "${YELLOW}ВАЖНО: При первом входе смените пароль администратора в профиле!${NC}"
    echo -e "${CYAN}Для подключения воркер-нод перейдите в панель: ${ACCESS_URL}/admin/nodes${NC}"
    echo "=================================================================="
else
    echo -e "${RED}[WARNING] Сервисы запущены, но проверка healthcheck бэкенда заняла больше времени, чем ожидалось.${NC}"
    echo "Проверьте логи командой: docker compose logs -f backend"
fi
