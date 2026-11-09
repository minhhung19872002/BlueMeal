import type { AppRoute } from './router';

/**
 * All authenticated routes. Each page is lazy-loaded. Keep in sync with navigation.tsx.
 * Permissions here only drive the friendly 403 page — the API enforces every permission server-side.
 */
export const appRoutes: AppRoute[] = [
  { path: '', page: () => import('@/features/dashboard/DashboardPage') },
  { path: 'profile', page: () => import('@/features/auth/ProfilePage') },
  { path: 'admin/version', page: () => import('@/features/admin/VersionPage'), perms: ['system.version'] },
];
