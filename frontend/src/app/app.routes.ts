import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { adminGuard } from './core/auth/admin.guard';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'dashboard',
    canActivate: [authGuard],
    loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent)
  },
  {
    path: 'shared',
    loadComponent: () => import('./features/shared-space/shared-space.component').then(m => m.SharedSpaceComponent)
  },
  {
    path: 'drive',
    canActivate: [authGuard],
    loadComponent: () => import('./features/drive/drive-layout/drive-layout.component').then(m => m.DriveLayoutComponent)
  },
  {
    path: 'admin',
    canActivate: [authGuard, adminGuard],
    loadComponent: () => import('./features/admin/admin-layout/admin-layout.component').then(m => m.AdminLayoutComponent),
    children: [
      {
        path: '',
        redirectTo: 'users',
        pathMatch: 'full'
      },
      {
        path: 'users',
        loadComponent: () => import('./features/admin/users-management/users-management.component').then(m => m.UsersManagementComponent)
      },
      {
        path: 'nodes',
        loadComponent: () => import('./features/admin/storage-nodes/storage-nodes.component').then(m => m.StorageNodesComponent)
      },
      {
        path: 'audit',
        loadComponent: () => import('./features/admin/audit-log/audit-log.component').then(m => m.AuditLogComponent)
      },
      {
        path: 'health',
        loadComponent: () => import('./features/admin/system-health/system-health.component').then(m => m.SystemHealthComponent)
      }
    ]
  },
  {
    path: '',
    redirectTo: 'drive',
    pathMatch: 'full'
  },
  {
    path: '**',
    redirectTo: 'drive'
  }
];
