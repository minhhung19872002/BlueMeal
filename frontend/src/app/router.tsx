import { lazy, type ComponentType, type ReactNode } from 'react';
import { createBrowserRouter, type RouteObject } from 'react-router';
import { MainLayout } from './layout/MainLayout';
import { RequireAuth, RequirePermission } from '@/shared/components/Guards';
import { ForbiddenPage, MaintenancePage, NotFoundPage, RouteErrorPage, ServerErrorPage } from './pages/ErrorPages';
import { appRoutes } from './routes';

const LoginPage = lazy(() => import('@/features/auth/LoginPage'));
const FirstLoginPage = lazy(() => import('@/features/auth/FirstLoginPage'));
const ForgotPasswordPage = lazy(() => import('@/features/auth/PasswordPages').then((m) => ({ default: m.ForgotPasswordPage })));
const ResetPasswordPage = lazy(() => import('@/features/auth/PasswordPages').then((m) => ({ default: m.ResetPasswordPage })));

/** Route entry: lazily loaded page (code-splitting per route — SPEC §11) protected by permissions. */
export interface AppRoute {
  path: string;
  page: () => Promise<{ default: ComponentType }>;
  perms?: string[];
}

function guarded(r: AppRoute): RouteObject {
  const Page = lazy(r.page);
  const el: ReactNode = <Page />;
  return { path: r.path, element: r.perms?.length ? <RequirePermission any={r.perms}>{el}</RequirePermission> : el };
}

export const router = createBrowserRouter([
  { path: '/login', element: <LoginPage />, errorElement: <RouteErrorPage /> },
  { path: '/first-login', element: <FirstLoginPage /> },
  { path: '/forgot-password', element: <ForgotPasswordPage /> },
  { path: '/reset-password', element: <ResetPasswordPage /> },
  { path: '/maintenance', element: <MaintenancePage /> },
  {
    path: '/',
    element: (
      <RequireAuth>
        <MainLayout />
      </RequireAuth>
    ),
    errorElement: <RouteErrorPage />,
    children: [
      ...appRoutes.map(guarded),
      { path: '403', element: <ForbiddenPage /> },
      { path: '500', element: <ServerErrorPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
]);
