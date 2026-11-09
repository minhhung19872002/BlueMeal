import type { AppRoute } from './router';

/**
 * All authenticated routes. Each page is lazy-loaded. Keep in sync with navigation.tsx.
 * Permissions here only drive the friendly 403 page — the API enforces every permission server-side.
 */
export const appRoutes: AppRoute[] = [
  { path: '', page: () => import('@/features/dashboard/DashboardPage') },
  { path: 'profile', page: () => import('@/features/auth/ProfilePage') },
  { path: 'admin/version', page: () => import('@/features/admin/VersionPage'), perms: ['system.version'] },
  { path: 'admin/organization', page: () => import('@/features/admin/OrganizationPage'), perms: ['organization.view'] },
  { path: 'admin/departments', page: () => import('@/features/admin/CommonCatalogPages').then((m) => ({ default: m.DepartmentsPage })), perms: ['department.view'] },
  { path: 'admin/employees', page: () => import('@/features/admin/EmployeesPage'), perms: ['employee.view'] },
  { path: 'admin/roles', page: () => import('@/features/admin/RolesPage'), perms: ['role.view'] },
  { path: 'admin/users', page: () => import('@/features/admin/UsersPage'), perms: ['user.view'] },
  { path: 'admin/sessions', page: () => import('@/features/admin/SecurityPages').then((m) => ({ default: m.SessionsPage })), perms: ['session.view'] },
  { path: 'admin/permissions', page: () => import('@/features/admin/SecurityPages').then((m) => ({ default: m.PermissionsPage })), perms: ['permission.view'] },
  { path: 'admin/audit-logs', page: () => import('@/features/admin/SecurityPages').then((m) => ({ default: m.AuditLogsPage })), perms: ['audit.view'] },
  { path: 'admin/settings', page: () => import('@/features/admin/SettingsPage'), perms: ['system.setting'] },
  { path: 'catalog/countries', page: () => import('@/features/admin/CommonCatalogPages').then((m) => ({ default: m.CountriesPage })), perms: ['common_catalog.view'] },
  { path: 'catalog/provinces', page: () => import('@/features/admin/CommonCatalogPages').then((m) => ({ default: m.ProvincesPage })), perms: ['common_catalog.view'] },
  { path: 'catalog/wards', page: () => import('@/features/admin/CommonCatalogPages').then((m) => ({ default: m.WardsPage })), perms: ['common_catalog.view'] },
  { path: 'catalog/units', page: () => import('@/features/admin/CommonCatalogPages').then((m) => ({ default: m.UnitsPage })), perms: ['common_catalog.view'] },
];
