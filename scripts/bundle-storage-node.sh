#!/usr/bin/env bash
# ==============================================================================
# 🌌 Omega Cloud — Storage Node Bundle Generator
# Упаковывает автономный микро-пакет ноды для переноса на удаленные серверы / NAS
# ==============================================================================
set -e

CYAN='\033[0;36m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST_DIR="$ROOT_DIR/dist"
BUNDLE_DIR="$DIST_DIR/omega-storage-node"
SRC_NODE_DIR="$ROOT_DIR/backend/src/Omega.StorageNode"

echo -e "${CYAN}"
echo "=================================================================="
echo "    📦 OMEGA CLOUD — СБОРКА АВТОНОМНОГО ПАКЕТА STORAGE NODE       "
echo "=================================================================="
echo -e "${NC}"

rm -rf "$BUNDLE_DIR" "$DIST_DIR/omega-storage-node.tar.gz" "$DIST_DIR/omega-storage-node.zip"
mkdir -p "$BUNDLE_DIR"

echo -e "${CYAN}[*] Копирование исходных файлов ноды...${NC}"

# Копируем всё кроме obj, bin, data, .vs
rsync -av --exclude 'bin' --exclude 'obj' --exclude 'data' --exclude '.vs' "$SRC_NODE_DIR/" "$BUNDLE_DIR/" 2>/dev/null || {
    cp -r "$SRC_NODE_DIR"/* "$BUNDLE_DIR/"
    rm -rf "$BUNDLE_DIR/bin" "$BUNDLE_DIR/obj" "$BUNDLE_DIR/data" "$BUNDLE_DIR/.vs" 2>/dev/null || true
}

chmod +x "$BUNDLE_DIR/install-node.sh" 2>/dev/null || true

echo -e "${CYAN}[*] Создание архива .tar.gz...${NC}"
cd "$DIST_DIR"
tar -czf "omega-storage-node.tar.gz" "omega-storage-node"

if command -v zip &> /dev/null; then
    zip -q -r "omega-storage-node.zip" "omega-storage-node"
fi

SIZE=$(du -h "omega-storage-node.tar.gz" | cut -f1)

echo -e "${GREEN}=================================================================="
echo " [✓] ПАКЕТ УСПЕШНО СОБРАН!"
echo " Путь к архиву: dist/omega-storage-node.tar.gz ($SIZE)"
echo "=================================================================="
echo -e "${NC}"
echo "Как задеплоить на удаленный сервер или NAS:"
echo ""
echo " 1. Скопируйте архив на удаленную машину:"
echo -e "    ${CYAN}scp dist/omega-storage-node.tar.gz user@your-nas:/opt/${NC}"
echo ""
echo " 2. На удаленной машине распакуйте и запустите установщик:"
echo -e "    ${CYAN}tar -xzf omega-storage-node.tar.gz && cd omega-storage-node${NC}"
echo -e "    ${CYAN}./install-node.sh${NC}"
echo ""
echo " 3. Введите токен регистрации из веб-панели Master-сервера."
echo "=================================================================="
