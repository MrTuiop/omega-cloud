import { Injectable, signal, computed } from '@angular/core';

export type AppLanguage = 'ru' | 'en' | 'zh';

export interface LanguageOption {
  code: AppLanguage;
  label: string;
  shortLabel: string;
}

export const SUPPORTED_LANGUAGES: LanguageOption[] = [
  { code: 'ru', label: 'Русский', shortLabel: 'RU' },
  { code: 'en', label: 'English', shortLabel: 'EN' },
  { code: 'zh', label: '中文 (简体)', shortLabel: 'ZH' }
];

const TRANSLATIONS: Record<AppLanguage, Record<string, string>> = {
  en: {
    // Navigation
    'nav.dashboard': 'Dashboard',
    'nav.drive': 'My Drive',
    'nav.shared': 'Shared Space',
    'nav.favorites': 'Favorites',
    'nav.trash': 'Trash',
    'nav.admin': 'Admin Console',
    'nav.logout': 'Sign Out',
    'nav.switchTheme': 'Switch Theme',
    'nav.language': 'Language',

    // Dashboard
    'dashboard.title': 'Dashboard',
    'dashboard.subtitle': 'Overview of your cloud storage, recent files and quick actions',
    'dashboard.recentFiles': 'Recent Files',
    'dashboard.noRecentFiles': 'No files uploaded yet.',
    'dashboard.storageStatus': 'Storage Status',
    'dashboard.storageUsed': 'Storage Used',
    'dashboard.storageAvailable': 'Available Space',
    'dashboard.quickActions': 'Quick Actions',
    'dashboard.uploadFiles': 'Upload Files',
    'dashboard.createFolder': 'New Folder',
    'dashboard.openDrive': 'Open My Drive',
    'dashboard.openShared': 'Open Shared Space',
    'dashboard.viewAll': 'View all files',
    'dashboard.usedOf': 'used of',
    'dashboard.usageHealthy': 'Healthy storage consumption',
    'dashboard.usageWarning': 'Approaching storage limit (>80%)',
    'dashboard.usageDanger': 'Critical storage capacity (>95%)',

    // Shared Space
    'shared.title': 'Shared Space',
    'shared.subtitle': 'Public files shared across the team or published for anonymous downloads',
    'shared.anonymousBadge': 'Read-Only Access',
    'shared.anonymousNotice': 'You are viewing Shared Space anonymously. Download files or sign in to upload and manage.',
    'shared.loginToUpload': 'Sign in to Upload',
    'shared.uploadShared': 'Upload to Shared Space',
    'shared.createSharedFolder': 'New Shared Folder',
    'shared.empty': 'No public files in Shared Space yet.',
    'shared.owner': 'Owner',
    'shared.public': 'Public',
    'shared.deleteConfirmTitle': 'Delete Shared Item',
    'shared.deleteConfirmMsg': 'Are you sure you want to delete this shared item? Only the author or administrators can perform this action.',

    // Upload & Queue
    'upload.queueTitle': 'Upload Queue Preview',
    'upload.queueDescription': 'Review files and folders before uploading to',
    'upload.totalFiles': 'Total Files',
    'upload.totalSize': 'Total Size',
    'upload.filterPlaceholder': 'Filter queue by name...',
    'upload.startUpload': 'Start Upload',
    'upload.clearAll': 'Clear Queue',
    'upload.remove': 'Remove',
    'upload.dropHint': 'Drop files or folders to upload',
    'upload.dropSub': 'Upload directly to',
    'upload.statusQueued': 'Queued',
    'upload.statusUploading': 'Uploading',
    'upload.statusCompleted': 'Completed',
    'upload.statusError': 'Error',
    'upload.emptyQueue': 'No files in upload queue.',

    // Conflicts
    'conflicts.title': 'File Name Conflicts Detected',
    'conflicts.subtitle': 'Some files already exist in this destination. Choose how to resolve conflicts:',
    'conflicts.fileName': 'Conflicting File',
    'conflicts.action': 'Resolution Action',
    'conflicts.replace': 'Replace',
    'conflicts.replaceDesc': 'Overwrite existing file',
    'conflicts.rename': 'Keep Both',
    'conflicts.renameDesc': 'Save copy with name (1)',
    'conflicts.skip': 'Skip',
    'conflicts.skipDesc': 'Do not upload this file',
    'conflicts.applyToAll': 'Apply this action to all conflicting files',
    'conflicts.confirm': 'Apply and Proceed',

    // Search
    'search.placeholder': 'Search files & folders...',
    'search.scopeEverywhere': 'Everywhere',
    'search.scopeCurrentFolder': 'Current Folder & Subfolders',
    'search.scope': 'Search Scope',
    'search.clear': 'Clear search',
    'search.noResults': 'No matching items found',
    'search.tryAdjusting': 'Try adjusting your search query or changing search scope.',

    // File Grid & List
    'files.name': 'Name',
    'files.size': 'Size',
    'files.modified': 'Last Modified',
    'files.actions': 'Actions',
    'files.emptyFolder': 'This folder is empty',
    'files.dragToUpload': 'Drag files here to upload',
    'files.download': 'Download',
    'files.delete': 'Delete',
    'files.rename': 'Rename',
    'files.move': 'Move',
    'files.details': 'Details',
    'files.share': 'Share',
    'files.selectedCount': 'item(s) selected',
    'files.downloadSelected': 'Download Zip',
    'files.deleteSelected': 'Delete Selected',
    'files.moveSelected': 'Move Selected',

    // Common
    'common.cancel': 'Cancel',
    'common.save': 'Save',
    'common.confirm': 'Confirm',
    'common.delete': 'Delete',
    'common.close': 'Close',
    'common.loading': 'Loading...',
    'common.success': 'Success',
    'common.error': 'Error',
    'common.signIn': 'Sign In',
    'common.admin': 'Administrator',
    'common.user': 'User',

    // Mobile
    'mobile.actions': 'Actions',
    'mobile.menu': 'Menu'
  },
  ru: {
    // Навигация
    'nav.dashboard': 'Главная',
    'nav.drive': 'Мой диск',
    'nav.shared': 'Общий раздел',
    'nav.favorites': 'Избранное',
    'nav.trash': 'Корзина',
    'nav.admin': 'Панель управления',
    'nav.logout': 'Выйти',
    'nav.switchTheme': 'Сменить тему',
    'nav.language': 'Язык',

    // Дашборд
    'dashboard.title': 'Главная страница',
    'dashboard.subtitle': 'Обзор вашего облачного диска, недавние файлы и быстрые действия',
    'dashboard.recentFiles': 'Недавние файлы',
    'dashboard.noRecentFiles': 'Файлы еще не загружены.',
    'dashboard.storageStatus': 'Статус хранилища',
    'dashboard.storageUsed': 'Занятое место',
    'dashboard.storageAvailable': 'Доступно места',
    'dashboard.quickActions': 'Быстрые действия',
    'dashboard.uploadFiles': 'Загрузить файлы',
    'dashboard.createFolder': 'Создать папку',
    'dashboard.openDrive': 'Перейти в Мой диск',
    'dashboard.openShared': 'Перейти в Общий раздел',
    'dashboard.viewAll': 'Посмотреть все файлы',
    'dashboard.usedOf': 'занято из',
    'dashboard.usageHealthy': 'Оптимальный уровень заполнения',
    'dashboard.usageWarning': 'Приближение к лимиту хранилища (>80%)',
    'dashboard.usageDanger': 'Критическая емкость хранилища (>95%)',

    // Общий раздел
    'shared.title': 'Общий раздел',
    'shared.subtitle': 'Публичные файлы команды и материалы с анонимным доступом к скачиванию',
    'shared.anonymousBadge': 'Только чтение',
    'shared.anonymousNotice': 'Вы просматриваете Общий раздел без авторизации. Вы можете просматривать и скачивать файлы. Войдите, чтобы загружать и управлять.',
    'shared.loginToUpload': 'Войти для загрузки',
    'shared.uploadShared': 'Загрузить в Общий раздел',
    'shared.createSharedFolder': 'Создать общую папку',
    'shared.empty': 'В общем разделе пока нет файлов.',
    'shared.owner': 'Владелец',
    'shared.public': 'Общий',
    'shared.deleteConfirmTitle': 'Удалить общий элемент',
    'shared.deleteConfirmMsg': 'Вы уверены, что хотите удалить этот общий элемент? Удалять могут только автор или администраторы.',

    // Загрузка и очередь
    'upload.queueTitle': 'Очередь загрузки',
    'upload.queueDescription': 'Предварительный просмотр файлов и папок перед отправкой в',
    'upload.totalFiles': 'Всего файлов',
    'upload.totalSize': 'Общий размер',
    'upload.filterPlaceholder': 'Фильтр по имени файла...',
    'upload.startUpload': 'Начать загрузку',
    'upload.clearAll': 'Очистить очередь',
    'upload.remove': 'Удалить',
    'upload.dropHint': 'Перетащите файлы или папки сюда',
    'upload.dropSub': 'Загрузка напрямую в',
    'upload.statusQueued': 'В очереди',
    'upload.statusUploading': 'Загрузка',
    'upload.statusCompleted': 'Завершено',
    'upload.statusError': 'Ошибка',
    'upload.emptyQueue': 'Очередь загрузки пуста.',

    // Коллизии
    'conflicts.title': 'Обнаружены коллизии имен файлов',
    'conflicts.subtitle': 'Файлы с такими именами уже существуют в этой папке. Выберите стратегию:',
    'conflicts.fileName': 'Конфликтующий файл',
    'conflicts.action': 'Действие',
    'conflicts.replace': 'Заменить',
    'conflicts.replaceDesc': 'Перезаписать существующий файл',
    'conflicts.rename': 'Сохранить оба',
    'conflicts.renameDesc': 'Добавить суффикс (1) к новому файлу',
    'conflicts.skip': 'Пропустить',
    'conflicts.skipDesc': 'Не загружать конфликтующий файл',
    'conflicts.applyToAll': 'Применить ко всем конфликтам',
    'conflicts.confirm': 'Применить и продолжить',

    // Поиск
    'search.placeholder': 'Поиск файлов и папок...',
    'search.scopeEverywhere': 'Везде',
    'search.scopeCurrentFolder': 'В текущей папке и подпапках',
    'search.scope': 'Область поиска',
    'search.clear': 'Очистить поиск',
    'search.noResults': 'Ничего не найдено',
    'search.tryAdjusting': 'Попробуйте изменить запрос или область поиска.',

    // Файлы
    'files.name': 'Имя',
    'files.size': 'Размер',
    'files.modified': 'Дата изменения',
    'files.actions': 'Действия',
    'files.emptyFolder': 'В этой папке пусто',
    'files.dragToUpload': 'Перетащите файлы сюда для загрузки',
    'files.download': 'Скачать',
    'files.delete': 'Удалить',
    'files.rename': 'Переименовать',
    'files.move': 'Переместить',
    'files.details': 'Свойства',
    'files.share': 'Поделиться',
    'files.selectedCount': 'элемент(ов) выбрано',
    'files.downloadSelected': 'Скачать архивом (ZIP)',
    'files.deleteSelected': 'Удалить выбранные',
    'files.moveSelected': 'Переместить выбранные',

    // Общие
    'common.cancel': 'Отмена',
    'common.save': 'Сохранить',
    'common.confirm': 'Подтвердить',
    'common.delete': 'Удалить',
    'common.close': 'Закрыть',
    'common.loading': 'Загрузка...',
    'common.success': 'Успешно',
    'common.error': 'Ошибка',
    'common.signIn': 'Войти',
    'common.admin': 'Администратор',
    'common.user': 'Пользователь',

    // Мобильные
    'mobile.actions': 'Действия',
    'mobile.menu': 'Меню'
  },
  zh: {
    // 导航
    'nav.dashboard': '控制台',
    'nav.drive': '我的网盘',
    'nav.shared': '共享空间',
    'nav.favorites': '收藏夹',
    'nav.trash': '回收站',
    'nav.admin': '管理控制台',
    'nav.logout': '退出登录',
    'nav.switchTheme': '切换主题',
    'nav.language': '语言',

    // 控制台
    'dashboard.title': '控制台',
    'dashboard.subtitle': '云存储总览、最近文件及快捷操作',
    'dashboard.recentFiles': '最近文件',
    'dashboard.noRecentFiles': '暂无上传文件。',
    'dashboard.storageStatus': '存储状态',
    'dashboard.storageUsed': '已用空间',
    'dashboard.storageAvailable': '可用空间',
    'dashboard.quickActions': '快捷操作',
    'dashboard.uploadFiles': '上传文件',
    'dashboard.createFolder': '新建文件夹',
    'dashboard.openDrive': '进入我的网盘',
    'dashboard.openShared': '进入共享空间',
    'dashboard.viewAll': '查看所有文件',
    'dashboard.usedOf': '已用配额 /',
    'dashboard.usageHealthy': '存储容量正常',
    'dashboard.usageWarning': '接近配额上限 (>80%)',
    'dashboard.usageDanger': '存储空间不足 (>95%)',

    // 共享空间
    'shared.title': '共享空间',
    'shared.subtitle': '团队公开文件与匿名直链下载空间',
    'shared.anonymousBadge': '只读访问',
    'shared.anonymousNotice': '您当前以匿名身份浏览共享空间。您可以浏览并下载文件，登录后可上传与管理。',
    'shared.loginToUpload': '登录后上传',
    'shared.uploadShared': '上传到共享空间',
    'shared.createSharedFolder': '新建共享文件夹',
    'shared.empty': '共享空间中暂无文件。',
    'shared.owner': '所有者',
    'shared.public': '公开',
    'shared.deleteConfirmTitle': '删除共享项目',
    'shared.deleteConfirmMsg': '确定要删除此共享项目吗？仅创建者或系统管理员有权执行此操作。',

    // 上传与队列
    'upload.queueTitle': '上传队列预览',
    'upload.queueDescription': '在上传至目标路径前确认文件和文件夹列表：',
    'upload.totalFiles': '文件总数',
    'upload.totalSize': '总容量',
    'upload.filterPlaceholder': '按名称筛选队列...',
    'upload.startUpload': '开始上传',
    'upload.clearAll': '清空队列',
    'upload.remove': '移除',
    'upload.dropHint': '拖放文件或文件夹至此处上传',
    'upload.dropSub': '直接上传到',
    'upload.statusQueued': '排队中',
    'upload.statusUploading': '上传中',
    'upload.statusCompleted': '已完成',
    'upload.statusError': '失败',
    'upload.emptyQueue': '上传队列为空。',

    // 冲突解决
    'conflicts.title': '检测到文件名冲突',
    'conflicts.subtitle': '目标目录中已存在同名文件。请选择冲突解决策略：',
    'conflicts.fileName': '冲突文件',
    'conflicts.action': '处理方式',
    'conflicts.replace': '替换',
    'conflicts.replaceDesc': '覆盖已存在的同名文件',
    'conflicts.rename': '保留两者',
    'conflicts.renameDesc': '新建副本后缀如 (1)',
    'conflicts.skip': '跳过',
    'conflicts.skipDesc': '跳过并取消此文件的上传',
    'conflicts.applyToAll': '将此选择应用到所有冲突文件',
    'conflicts.confirm': '应用并继续',

    // 搜索
    'search.placeholder': '搜索文件与文件夹...',
    'search.scopeEverywhere': '全部位置',
    'search.scopeCurrentFolder': '当前目录及子目录',
    'search.scope': '搜索范围',
    'search.clear': '清除搜索',
    'search.noResults': '未找到匹配项',
    'search.tryAdjusting': '请尝试调整搜索关键词或搜索范围。',

    // 文件列表
    'files.name': '文件名',
    'files.size': '大小',
    'files.modified': '修改时间',
    'files.actions': '操作',
    'files.emptyFolder': '此文件夹为空',
    'files.dragToUpload': '拖放文件到此处上传',
    'files.download': '下载',
    'files.delete': '删除',
    'files.rename': '重命名',
    'files.move': '移动',
    'files.details': '详细信息',
    'files.share': '共享',
    'files.selectedCount': '项已选中',
    'files.downloadSelected': '打包下载 (ZIP)',
    'files.deleteSelected': '删除选中项',
    'files.moveSelected': '移动选中项',

    // 通用
    'common.cancel': '取消',
    'common.save': '保存',
    'common.confirm': '确定',
    'common.delete': '删除',
    'common.close': '关闭',
    'common.loading': '加载中...',
    'common.success': '成功',
    'common.error': '错误',
    'common.signIn': '登录',
    'common.admin': '管理员',
    'common.user': '普通用户',

    // 移动端
    'mobile.actions': '操作项',
    'mobile.menu': '菜单'
  }
};

const STORAGE_KEY = 'omega_cloud_lang';

@Injectable({
  providedIn: 'root'
})
export class I18nService {
  readonly supportedLanguages = SUPPORTED_LANGUAGES;

  private readonly _currentLang = signal<AppLanguage>(this.getInitialLanguage());
  readonly currentLang = computed(() => this._currentLang());

  constructor() {
    this.applyHtmlLang(this._currentLang());
  }

  setLanguage(lang: AppLanguage): void {
    if (this._currentLang() === lang) return;
    this._currentLang.set(lang);
    try {
      localStorage.setItem(STORAGE_KEY, lang);
    } catch {
      // LocalStorage access may be restricted
    }
    this.applyHtmlLang(lang);
  }

  t(key: string, params?: Record<string, string | number>): string {
    const lang = this._currentLang();
    const dictionary = TRANSLATIONS[lang] || TRANSLATIONS['ru'];
    let text = dictionary[key] ?? TRANSLATIONS['en'][key] ?? key;

    if (params) {
      for (const [k, v] of Object.entries(params)) {
        text = text.replace(new RegExp(`{${k}}`, 'g'), String(v));
      }
    }

    return text;
  }

  private getInitialLanguage(): AppLanguage {
    try {
      const saved = localStorage.getItem(STORAGE_KEY) as AppLanguage | null;
      if (saved && ['ru', 'en', 'zh'].includes(saved)) {
        return saved;
      }

      const browserLang = (navigator.language || '').toLowerCase();
      if (browserLang.startsWith('ru')) return 'ru';
      if (browserLang.startsWith('zh')) return 'zh';
      return 'en';
    } catch {
      return 'ru';
    }
  }

  private applyHtmlLang(lang: AppLanguage): void {
    if (typeof document !== 'undefined' && document.documentElement) {
      document.documentElement.lang = lang;
    }
  }
}
