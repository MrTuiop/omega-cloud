# Omega Cloud ☁️

**Omega Cloud** — это современная распределенная облачная платформа хранения данных корпоративного уровня с децентрализованными узлами хранения (Storage Nodes), отказоустойчивой архитектурой микросервисов и реактивным фронтендом на Angular 19 в стиле Liquid Glassmorphism.

---

## 🏛️ Архитектура системы

Архитектура Omega Cloud построена на модульном разделении ответственности:

```
                          ┌───────────────────────────┐
                          │   Клиент / Браузер        │
                          │   (Angular 19 SPA)        │
                          └─────────────┬─────────────┘
                                        │ HTTPS (80/443)
                                        ▼
                          ┌───────────────────────────┐
                          │     Caddy Reverse Proxy   │
                          │  (Auto TLS / Static Host) │
                          └──────┬─────────────┬──────┘
                                 │             │
                /api/*, /hubs/*  │             │  /* (SPA static files)
                                 ▼             ▼
      ┌───────────────────────────────┐   ┌───────────────────────────┐
      │  OmegaCloud Master API        │   │  Angular 19 Frontend Dist │
      │  (ASP.NET Core 10 / C# 13)    │   │  (SSR/SPA Nginx/Alpine)   │
      └──────┬──────────────┬─────────┘   └───────────────────────────┘
             │              │
             │ Heartbeats   │ Database Connection
             │ & Proxy      │ (Npgsql EF Core)
             ▼              ▼
  ┌───────────────────┐  ┌────────────────────────────────────────┐
  │ Storage Node(s)   │  │ PostgreSQL 16 Alpine                   │
  │ (ASP.NET Daemon)  │  │ (Users, Quotas, Files, Nodes, Sessions)│
  └───────────────────┘  └────────────────────────────────────────┘
```

### Компоненты системы:
1. **Master Backend (`OmegaCloud.Api` / `OmegaCloud.Application` / `OmegaCloud.Domain` / `OmegaCloud.Infrastructure`)**:
   - Построен на **.NET 10 (C# 13)** и Clean Architecture.
   - JWT-аутентификация с Refresh Token и активным трекингом сессий в БД.
   - SignalR хаб (`/hubs/events`) для мгновенных уведомлений об изменении файлов и статусов нод.
   - Резюмируемая чанковая загрузка (Resumable Chunked Upload) с дедупликацией по SHA-256.
   - Балансировка и роутинг нагрузки между узлами хранения.
   - Контроль квот пользователей с защитой от превышения физической емкости нод.

2. **Storage Node Daemon (`Omega.StorageNode`)**:
   - Автономный легковесный демон для децентрализованного хранения файлов.
   - Автоматическая регистрация в Master API по одноразовому токену (`RegistrationKey`).
   - Периодические heartbeat-запросы со сбором телеметрии диска (доступная/занятая емкость).
   - Поддержка прямого стриминга и работы за NAT (reverse stream).

3. **Frontend SPA (`OmegaCloud Frontend`)**:
   - Построен на **Angular 19** со standalone-компонентами и реактивными сигналами (`signals`, `computed`).
   - Фирменный дизайн-код **Liquid Glass**: эффект матового стекла, плавные градиенты, backdrop blur, адаптивная темная/светлая тема.
   - Виртуализированное файловое дерево, перетаскивание (Drag & Drop), резиновая рамка выделения (Marquee select), горячие клавиши.

4. **Инфраструктура**:
   - **PostgreSQL 16**: хранение метаданных пользователей, квот, файлов, папок, корзины, аудита и сессий.
   - **Caddy 2**: автоматическое управление SSL/TLS-сертификатами, проксирование WebSocket/SignalR и отдача SPA.

---

## 🚀 Новые возможности Release 1.1

### 1. Продвинутая загрузка папок и разрешение коллизий
- **Drop папок целиком**: рекурсивный обход директорий через `webkitGetAsEntry` с сохранением относительных путей.
- **Queue Preview Modal**: расчет суммарного объема, фильтрация файлов по поисковому запросу, удаление файлов из очереди перед стартом.
- **Collision Resolution Modal**: автоматическая детекция конфликтов имен с выбором стратегий:
  - **Заменить (Replace)**: перезапись существующего файла новой версией.
  - **Сохранить оба (Keep both / Rename)**: автоматическое переименование с суффиксом `(1)`, `(2)`.
  - **Пропустить (Skip)**: пропуск загрузки конфликтующего файла.
  - Переключатель *«Применить ко всем конфликтам»*.

### 2. Общий раздел (Shared Space UI — `/shared`)
- Публичный доступ: любой посетитель может просматривать и скачивать общедоступные файлы без входа в систему.
- Авторизованный режим: авторизованные пользователи могут загружать файлы в общий раздел и создавать папки через drag-and-drop.
- Права доступа: визуальный бейдж владельца (`You` / имя автора), удаление доступно только владельцу или администратору.

### 3. Главная страница (Dashboard — `/dashboard`)
- **Статус хранилища**: информативный виджет использования квоты с процентным индикатором и статусом емкости.
- **Быстрые действия (Quick Actions)**: моментальные кнопки «Загрузить файл», «Создать папку», «Общий раздел».
- **Недавние файлы (Recent Files)**: интерактивный список последних загруженных или обновленных файлов с прямым скачиванием.

### 4. Многоязычность (i18n Localization)
- Встроенная поддержка трех языков: **Русский (RU)**, **Английский (EN)**, **Китайский (ZH)**.
- Сервис `I18nService` и пайп `translate` для реактивной смены языка без перезагрузки страницы.
- Постоянное сохранение выбранного языка в `localStorage`.

### 5. Расширенный поиск (Extended Search)
- Селектор области поиска: **«Везде» (Everywhere)** vs **«Текущая папка и подпапки» (Current Folder & Subfolders)**.
- Рекурсивный поиск файлов и папок с ограничением по выбранной ветке дерева.

### 6. Production Polish & Mobile UX
- Удален тестовый хинт с логином/паролем со страницы авторизации.
- Мобильная адаптация: минимальный размер touch-target 44px, скрываемый боковой drawer, нижняя шторка действий (Action Sheet).
- Фирменные векторные фавиконы `favicon.ico` и `favicon.png`.

---

## 🛠️ Запуск через Docker Compose

### 1. Предварительные требования
- [Docker](https://docs.docker.com/get-docker/) (24.0+)
- [Docker Compose](https://docs.docker.com/compose/) (v2+)

### 2. Быстрый старт Master-кластера

Клонируйте репозиторий и создайте файл `.env`:

```bash
cp .env.example .env
```

Запустите контейнеры:

```bash
docker compose up -d --build
```

После старта будут доступны следующие сервисы:
- Веб-интерфейс Omega Cloud: `http://localhost` (или `https://localhost` при настроенном TLS)
- API Swagger / Документация: `http://localhost:5000/swagger`
- PostgreSQL: `localhost:5432`

### 3. Подключение Storage Node (Узла хранения)

1. Откройте панель администратора: `http://localhost/admin/nodes`
2. Сгенерируйте **Токен регистрации узла** (Node Registration Token).
3. Перейдите на сервер узла хранения в папку `backend/src/Omega.StorageNode/`:
4. Настройте переменные окружения и запустите ноду:

```bash
NODE_REGISTRATION_KEY="<TOKEN>" MASTER_BASE_URL="http://master-server:5000" docker compose up -d
```

Нода автоматически зарегистрируется в мастере и станет доступной для хранения файлов.

---

## 📋 Таблица переменных окружения

### Master Service (`docker-compose.yml`)

| Переменная | По умолчанию | Описание |
|---|---|---|
| `POSTGRES_DB` | `omega_cloud` | Имя базы данных PostgreSQL |
| `POSTGRES_USER` | `omega_admin` | Пользователь базы данных |
| `POSTGRES_PASSWORD` | *secret* | Пароль базы данных PostgreSQL |
| `POSTGRES_PORT` | `5432` | Порт PostgreSQL на хосте |
| `POSTGRES_DATA_PATH`| `./data/postgres` | Путь сохранения данных PostgreSQL |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Среда ASP.NET Core (`Production` / `Development`) |
| `JWT_SECRET` | *secret* | Секретный ключ подписи JWT (мин. 32 символа) |
| `JWT_ISSUER` | `OmegaCloudMaster`| Эмитент JWT токена |
| `JWT_AUDIENCE` | `OmegaCloudClients`| Получатель JWT токена |
| `JWT_ACCESS_MINUTES` | `15` | Время жизни Access Token в минутах |
| `JWT_REFRESH_DAYS` | `30` | Время жизни Refresh Token в днях |
| `ADMIN_USERNAME` | `admin` | Логин начального администратора |
| `ADMIN_EMAIL` | `admin@omegacloud.local` | Email начального администратора |
| `ADMIN_PASSWORD` | `AdminPassword123!` | Пароль начального администратора |
| `LOCAL_STORAGE_PATH`| `./data/storage` | Локальное хранилище Master ноды |
| `DOMAIN` | `localhost` | Домен для Caddy reverse proxy |
| `CADDY_TLS` | `internal` | Режим TLS Caddy (`internal` для self-signed / email для Let's Encrypt) |

### Storage Node Daemon (`Omega.StorageNode`)

| Переменная | По умолчанию | Описание |
|---|---|---|
| `STORAGE_NODE_PORT` | `5001` | Порт HTTP сервиса хранилища |
| `MASTER_BASE_URL` | `http://master:5000` | Базовый URL мастер-сервера |
| `NODE_REGISTRATION_KEY` | *(пусто)* | Одноразовый токен регистрации узла |
| `NODE_NAME` | `Omega-Storage-Node` | Отображаемое имя ноды |
| `STORAGE_PATH` | `./data/storage` | Локальный путь монтирования хранилища |

---

## 🔌 Сводка API эндпоинтов

### Аутентификация и Сессии (`/api/auth`)
- `POST /api/auth/login` — Авторизация пользователя (выдача JWT пары).
- `POST /api/auth/register` — Регистрация нового аккаунта.
- `POST /api/auth/refresh` — Обновление пары токенов по Refresh Token.
- `POST /api/auth/revoke` — Отзыв сессии и логаут.
- `GET  /api/auth/me` — Получение профиля текущего пользователя и квоты.
- `GET  /api/auth/sessions` — Список всех активных сессий пользователя.
- `DELETE /api/auth/sessions/{id}` — Принудительное завершение конкретной сессии (разрыв сокета).

### Файлы и Папки (`/api/files`, `/api/folders`)
- `GET  /api/folders/content` — Дерево директории: папки, файлы, хлебные крошки и квота.
- `POST /api/folders` — Создание папки.
- `PATCH /api/folders/{id}` — Переименование или изменение цвета папки.
- `DELETE /api/folders/{id}` — Перемещение папки в корзину.
- `POST /api/files/upload` — Прямая загрузка небольших файлов.
- `GET  /api/files/{id}/metadata` — Детальные метаданные файла и ноды хранения.
- `GET  /api/files/{id}/download` — Скачивание файла.
- `GET  /api/files/{id}/stream` — Стриминг аудио/видео контента с поддержкой HTTP Range.
- `GET  /api/files/{id}/thumbnail` — Генерация и отдача превью/миниатюр файлов.
- `PATCH /api/files/{id}` — Переименование или обновление флагов файла.
- `DELETE /api/files/{id}` — Перемещение файла в корзину.
- `POST /api/files/check-conflicts` — Проверка имен файлов на коллизии в целевой папке.
- `POST /api/files/check-conflict-details` — Подробные данные конфликтующих файлов (размер, дата).
- `POST /api/files/download-zip` — Пакетное скачивание файлов и папок в едином ZIP-архиве.
- `POST /api/files/bulk-delete` — Пакетное удаление выбранных элементов в корзину.
- `POST /api/files/bulk-move` — Пакетное перемещение выбранных элементов в целевую папку.
- `GET  /api/files/recent` — Список последних загруженных/измененных файлов для дашборда.
- `GET  /api/files/search` — Полнотекстовый поиск по файлам и папкам с поддержкой области (`everywhere` / `current`).

### Резюмируемая загрузка (`/api/upload`)
- `POST   /api/upload/init` — Инициализация сессии загрузки большого файла.
- `POST   /api/upload/chunk` — Загрузка отдельного чанка данных с контрольной суммой.
- `POST   /api/upload/complete` — Сборка чанков, валидация SHA-256 и регистрация файла.
- `DELETE /api/upload/{sessionId}` — Отмена и очистка незавершенной сессии.

### Общий раздел (`/api/shared`)
- `GET    /api/shared/folders` — Анонимный/публичный просмотр содержимого общего раздела.
- `GET    /api/shared/files/{id}/metadata` — Публичные метаданные общего файла.
- `GET    /api/shared/files/{id}/download` — Прямое публичное скачивание файла.
- `POST   /api/shared/upload` — Загрузка файла в общий раздел (для авторизованных).
- `POST   /api/shared/folders` — Создание папки в общем разделе (для авторизованных).
- `DELETE /api/shared/files/{id}` — Удаление файла из общего раздела (автор или админ).
- `DELETE /api/shared/folders/{id}` — Удаление папки из общего раздела (автор или админ).

### Корзина (`/api/trash`)
- `GET    /api/trash` — Просмотр удаленных файлов и папок в корзине.
- `POST   /api/trash/restore/{id}` — Восстановление элемента из корзины.
- `DELETE /api/trash/{id}` — Окончательное безвозвратное удаление элемента с освобождением места.
- `DELETE /api/trash/empty` — Полная очистка корзины.

### Административная панель (`/api/admin`)
- `GET    /api/admin/users` — Список всех пользователей системы.
- `POST   /api/admin/users` — Создание пользователя с проверкой суммарной емкости нод.
- `PUT    /api/admin/users/{id}` — Обновление пользователя, роли и лимита квоты.
- `DELETE /api/admin/users/{id}` — Блокировка/удаление пользователя.
- `GET    /api/admin/nodes` — Мониторинг всех зарегистрированных Storage Nodes.
- `POST   /api/admin/nodes/token` — Выпуск одноразового токена регистрации новой ноды.
- `PATCH  /api/admin/nodes/{id}` — Включение/отключение ноды, изменение приоритета.
- `DELETE /api/admin/nodes/{id}` — Удаление узла из кластера.
- `POST   /api/admin/nodes/{id}/ping` — Проверка доступности ноды по сети.
- `GET    /api/admin/health` — Комплексный статус здоровья системы и сервисов.
- `GET    /api/admin/audit` — Журнал аудита действий пользователей и администраторов.

---

## 🧪 Запуск автоматических тестов

### Тесты бэкенда (.NET Core xUnit):
```bash
dotnet test backend/OmegaCloud.sln
```
*Результат: 44 теста StorageNode + 316 тестов OmegaCloud.Api = 360 тестов успешно пройдено.*

### Тесты фронтенда (Karma + Jasmine + Headless Chrome):
```bash
cd frontend
npm test -- --watch=false --browsers=ChromeHeadless
```
*Результат: 177 тестов успешно пройдено.*

### Сборка фронтенда в Production:
```bash
cd frontend
npm run build
```

---

## 📄 Лицензия

Omega Cloud распространяется под лицензией MIT.
