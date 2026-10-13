import { Component, Input, Output, EventEmitter, OnChanges, SimpleChanges, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../../core/services/api.service';
import { I18nService } from '../../../core/services/i18n.service';
import { FolderItem } from '../../../core/models/file.models';
import { GlassModalComponent } from '../../../shared/components/glass-modal/glass-modal.component';
import { GlassButtonComponent } from '../../../shared/components/glass-button/glass-button.component';
import { TranslatePipe } from '../../../shared/pipes/translate.pipe';

export interface SearchScopeResult {
  scope: 'everywhere' | 'personal' | 'shared' | 'custom';
  folderIds: string[];
  label: string;
}

interface FolderTreeNode {
  folder: FolderItem;
  children: FolderTreeNode[];
  level: number;
}

@Component({
  selector: 'app-search-scope-modal',
  standalone: true,
  imports: [CommonModule, GlassModalComponent, GlassButtonComponent, TranslatePipe],
  template: `
    <app-glass-modal
      [isOpen]="isOpen"
      [title]="'search.scopeTitle' | translate"
      (close)="onCancel()"
    >
      <div class="search-scope-container">
        <!-- Quick Preset Tabs -->
        <div class="presets-section">
          <span class="section-label">{{ 'search.quickSelect' | translate }}</span>
          <div class="preset-buttons">
            <button
              type="button"
              class="preset-btn"
              [class.preset-btn--active]="activeScope === 'everywhere'"
              (click)="selectPreset('everywhere')"
            >
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="11" cy="11" r="8"></circle>
                <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
              </svg>
              <span>{{ 'search.scopeEverywhere' | translate }}</span>
            </button>

            <button
              type="button"
              class="preset-btn"
              [class.preset-btn--active]="activeScope === 'personal'"
              (click)="selectPreset('personal')"
            >
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
              </svg>
              <span>{{ 'search.scopePersonal' | translate }}</span>
            </button>

            <button
              type="button"
              class="preset-btn"
              [class.preset-btn--active]="activeScope === 'shared'"
              (click)="selectPreset('shared')"
            >
              <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                <circle cx="18" cy="5" r="3"></circle>
                <circle cx="6" cy="12" r="3"></circle>
                <circle cx="18" cy="19" r="3"></circle>
                <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line>
                <line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line>
              </svg>
              <span>{{ 'search.scopeShared' | translate }}</span>
            </button>
          </div>
        </div>

        <div class="divider"></div>

        <!-- Interactive Folder Tree -->
        <div class="tree-section">
          <div class="tree-header">
            <span class="section-label">{{ 'search.selectSpecificFolders' | translate }}</span>
            @if (tempSelectedFolderIds.size > 0) {
              <span class="selected-pill">{{ 'search.selectedFoldersCount' | translate:{ count: tempSelectedFolderIds.size } }}</span>
            }
          </div>

          @if (isLoading) {
            <div class="tree-loading">
              <span>{{ 'search.loadingTree' | translate }}</span>
            </div>
          } @else {
            <div class="tree-container">
              <!-- Personal Drive Group -->
              <div class="tree-group">
                <div class="tree-group-title">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path>
                  </svg>
                  <span>{{ 'search.personalDrive' | translate }}</span>
                </div>

                @if (personalTreeNodes.length === 0) {
                  <div class="tree-empty">{{ 'search.noPersonalFolders' | translate }}</div>
                } @else {
                  <ng-container *ngTemplateOutlet="folderTreeTpl; context: { nodes: personalTreeNodes }"></ng-container>
                }
              </div>

              <!-- Shared Space Group -->
              <div class="tree-group">
                <div class="tree-group-title">
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
                    <circle cx="18" cy="5" r="3"></circle>
                    <circle cx="6" cy="12" r="3"></circle>
                    <circle cx="18" cy="19" r="3"></circle>
                    <line x1="8.59" y1="13.51" x2="15.42" y2="17.49"></line>
                    <line x1="15.41" y1="6.51" x2="8.59" y2="10.49"></line>
                  </svg>
                  <span>{{ 'search.sharedSpace' | translate }}</span>
                </div>

                @if (sharedTreeNodes.length === 0) {
                  <div class="tree-empty">{{ 'search.noSharedFolders' | translate }}</div>
                } @else {
                  <ng-container *ngTemplateOutlet="folderTreeTpl; context: { nodes: sharedTreeNodes }"></ng-container>
                }
              </div>
            </div>
          }
        </div>
      </div>

      <!-- Recursive Node Template -->
      <ng-template #folderTreeTpl let-nodes="nodes">
        @for (node of nodes; track node.folder.id) {
          <div class="tree-node" [style.padding-left.px]="node.level * 18">
            <button
              type="button"
              class="expand-btn"
              [class.expand-btn--empty]="node.children.length === 0"
              (click)="toggleExpand(node.folder.id, $event)"
            >
              @if (node.children.length > 0) {
                <span>{{ isExpanded(node.folder.id) ? '▼' : '▶' }}</span>
              }
            </button>

            <label class="node-label">
              <input
                type="checkbox"
                class="node-checkbox"
                [checked]="isFolderChecked(node.folder.id)"
                (change)="onFolderToggle(node.folder.id, $event)"
              />
              <span class="folder-icon" [style.color]="node.folder.color || 'var(--omega-accent)'">
                <svg width="16" height="16" viewBox="0 0 24 24" fill="currentColor">
                  <path d="M10 4H4c-1.1 0-1.99.9-1.99 2L2 18c0 1.1.9 2 2 2h16c1.1 0 2-.9 2-2V8c0-1.1-.9-2-2-2h-8l-2-2z"/>
                </svg>
              </span>
              <span class="folder-name">{{ node.folder.name }}</span>
            </label>
          </div>

          @if (isExpanded(node.folder.id) && node.children.length > 0) {
            <ng-container *ngTemplateOutlet="folderTreeTpl; context: { nodes: node.children }"></ng-container>
          }
        }
      </ng-template>

      <!-- Modal Footer -->
      <div footer class="modal-footer-actions">
        <app-glass-button variant="secondary" (btnClick)="onCancel()">
          {{ 'common.cancel' | translate }}
        </app-glass-button>
        <app-glass-button variant="primary" (btnClick)="onApply()">
          {{ 'search.applyScope' | translate }}
        </app-glass-button>
      </div>
    </app-glass-modal>
  `,
  styles: [`
    @use 'styles/glass' as *;

    .search-scope-container {
      display: flex;
      flex-direction: column;
      gap: 16px;
      max-height: 65vh;
      min-width: 380px;
    }

    .section-label {
      font-size: 12px;
      font-weight: 600;
      color: var(--omega-text-muted);
      text-transform: uppercase;
      letter-spacing: 0.05em;
    }

    .presets-section {
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .preset-buttons {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }

    .preset-btn {
      @include glass-surface;
      padding: 8px 14px;
      border-radius: var(--omega-radius-md);
      display: inline-flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      font-weight: 500;
      color: var(--omega-text-secondary);
      border: 1px solid var(--omega-glass-border-subtle);
      cursor: pointer;
      transition: all 0.2s var(--omega-ease-smooth);

      &:hover {
        background: var(--omega-glass-bg-hover);
        color: var(--omega-text-primary);
        border-color: var(--omega-accent);
      }

      &--active {
        background: var(--omega-accent-subtle) !important;
        border-color: var(--omega-accent) !important;
        color: var(--omega-accent) !important;
        box-shadow: 0 0 12px rgba(0, 113, 227, 0.25);
      }
    }

    .divider {
      height: 1px;
      background: var(--omega-glass-border-subtle);
      width: 100%;
    }

    .tree-section {
      display: flex;
      flex-direction: column;
      gap: 10px;
      min-height: 180px;
    }

    .tree-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .selected-pill {
      font-size: 11px;
      font-weight: 600;
      color: var(--omega-accent);
      background: var(--omega-accent-subtle);
      padding: 2px 8px;
      border-radius: 12px;
      border: 1px solid var(--omega-accent);
    }

    .tree-loading {
      padding: 24px 0;
      text-align: center;
      color: var(--omega-text-muted);
      font-size: 13px;
    }

    .tree-container {
      max-height: 280px;
      overflow-y: auto;
      display: flex;
      flex-direction: column;
      gap: 14px;
      padding-right: 4px;
    }

    .tree-group {
      display: flex;
      flex-direction: column;
      gap: 4px;
    }

    .tree-group-title {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      font-weight: 600;
      color: var(--omega-text-primary);
      padding: 4px 0;
    }

    .tree-empty {
      font-size: 12px;
      color: var(--omega-text-muted);
      padding-left: 14px;
      font-style: italic;
    }

    .tree-node {
      display: flex;
      align-items: center;
      gap: 4px;
      padding: 3px 6px;
      border-radius: 6px;
      transition: background 0.15s ease;

      &:hover {
        background: var(--omega-glass-bg-hover);
      }
    }

    .expand-btn {
      background: none;
      border: none;
      color: var(--omega-text-muted);
      font-size: 10px;
      width: 18px;
      height: 18px;
      display: flex;
      align-items: center;
      justify-content: center;
      cursor: pointer;
      padding: 0;

      &--empty {
        cursor: default;
        opacity: 0;
      }
    }

    .node-label {
      display: flex;
      align-items: center;
      gap: 8px;
      cursor: pointer;
      min-width: 0;
      flex: 1;
    }

    .node-checkbox {
      appearance: none;
      -webkit-appearance: none;
      width: 16px;
      height: 16px;
      border-radius: 3px;
      border: 1.5px solid var(--omega-glass-border, rgba(255, 255, 255, 0.4));
      background: rgba(255, 255, 255, 0.12);
      cursor: pointer;
      position: relative;
      display: inline-flex;
      align-items: center;
      justify-content: center;
      transition: all 0.15s ease;
      flex-shrink: 0;

      &:hover {
        border-color: var(--omega-accent);
      }

      &:checked {
        background: var(--omega-accent);
        border-color: var(--omega-accent);

        &::after {
          content: '';
          display: block;
          width: 4px;
          height: 8px;
          border: solid #ffffff;
          border-width: 0 2px 2px 0;
          transform: rotate(45deg);
          margin-bottom: 2px;
        }
      }
    }

    .folder-icon {
      display: flex;
      align-items: center;
      justify-content: center;
      flex-shrink: 0;
    }

    .folder-name {
      font-size: 13px;
      color: var(--omega-text-primary);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .modal-footer-actions {
      display: flex;
      align-items: center;
      justify-content: flex-end;
      gap: 10px;
      width: 100%;
    }
  `]
})
export class SearchScopeModalComponent implements OnChanges {
  @Input() isOpen = false;
  @Input() initialScope: 'everywhere' | 'personal' | 'shared' | 'custom' = 'everywhere';
  @Input() initialFolderIds: string[] = [];

  @Output() close = new EventEmitter<void>();
  @Output() applyScope = new EventEmitter<SearchScopeResult>();

  private readonly apiService = inject(ApiService);
  private readonly i18n = inject(I18nService);

  activeScope: 'everywhere' | 'personal' | 'shared' | 'custom' = 'everywhere';
  tempSelectedFolderIds = new Set<string>();
  expandedFolderIds = new Set<string>();

  personalTreeNodes: FolderTreeNode[] = [];
  sharedTreeNodes: FolderTreeNode[] = [];
  allFoldersMap = new Map<string, FolderItem>();
  isLoading = false;

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['isOpen'] && this.isOpen) {
      this.activeScope = this.initialScope || 'everywhere';
      this.tempSelectedFolderIds = new Set(this.initialFolderIds || []);
      this.loadTreeData();
    }
  }

  loadTreeData(): void {
    this.isLoading = true;
    this.apiService.getFolderTree().subscribe({
      next: (res) => {
        this.isLoading = false;
        this.allFoldersMap.clear();

        for (const f of res.personalFolders || []) {
          this.allFoldersMap.set(f.id, f);
        }
        for (const f of res.sharedFolders || []) {
          this.allFoldersMap.set(f.id, f);
        }

        this.personalTreeNodes = this.buildTree(res.personalFolders || []);
        this.sharedTreeNodes = this.buildTree(res.sharedFolders || []);

        // Expand root nodes by default
        for (const n of this.personalTreeNodes) {
          this.expandedFolderIds.add(n.folder.id);
        }
        for (const n of this.sharedTreeNodes) {
          this.expandedFolderIds.add(n.folder.id);
        }

        // Auto-expand ancestors of selected folders so they are immediately visible
        for (const fId of this.tempSelectedFolderIds) {
          let curr = this.allFoldersMap.get(fId);
          while (curr && curr.parentFolderId) {
            this.expandedFolderIds.add(curr.parentFolderId);
            curr = this.allFoldersMap.get(curr.parentFolderId);
          }
        }
      },
      error: () => {
        this.isLoading = false;
      }
    });
  }

  private buildTree(folders: FolderItem[], parentId: string | null = null, level = 0): FolderTreeNode[] {
    if (level > 20) {
      return [];
    }
    const folderIdSet = new Set(folders.map(f => f.id));
    return folders
      .filter(f => {
        const pId = f.parentFolderId || null;
        if (parentId === null) {
          return pId === null || !folderIdSet.has(pId);
        }
        return pId === parentId;
      })
      .map(f => ({
        folder: f,
        level,
        children: this.buildTree(folders, f.id, level + 1)
      }));
  }

  selectPreset(preset: 'everywhere' | 'personal' | 'shared'): void {
    this.activeScope = preset;
    this.tempSelectedFolderIds.clear();
  }

  isFolderChecked(folderId: string): boolean {
    return this.tempSelectedFolderIds.has(folderId);
  }

  onFolderToggle(folderId: string, event: Event): void {
    const checked = (event.target as HTMLInputElement).checked;
    if (checked) {
      this.tempSelectedFolderIds.add(folderId);
      this.activeScope = 'custom';
    } else {
      this.tempSelectedFolderIds.delete(folderId);
      if (this.tempSelectedFolderIds.size === 0) {
        this.activeScope = 'everywhere';
      }
    }
  }

  isExpanded(folderId: string): boolean {
    return this.expandedFolderIds.has(folderId);
  }

  toggleExpand(folderId: string, event: MouseEvent): void {
    event.stopPropagation();
    if (this.expandedFolderIds.has(folderId)) {
      this.expandedFolderIds.delete(folderId);
    } else {
      this.expandedFolderIds.add(folderId);
    }
  }

  onCancel(): void {
    this.close.emit();
  }

  onApply(): void {
    let label = '';
    const folderIds = Array.from(this.tempSelectedFolderIds);

    if (this.activeScope === 'custom' && folderIds.length > 0) {
      if (folderIds.length === 1) {
        const f = this.allFoldersMap.get(folderIds[0]);
        label = f ? `[${f.name}]` : `[${this.i18n.translate('search.folder')}]`;
      } else {
        label = this.i18n.translate('search.foldersWithCount', { count: folderIds.length });
      }
    } else if (this.activeScope === 'personal') {
      label = this.i18n.translate('search.scopePersonal');
    } else if (this.activeScope === 'shared') {
      label = this.i18n.translate('search.scopeShared');
    } else {
      this.activeScope = 'everywhere';
      label = this.i18n.translate('search.scopeEverywhere');
    }

    this.applyScope.emit({
      scope: this.activeScope,
      folderIds,
      label
    });
    this.close.emit();
  }
}
