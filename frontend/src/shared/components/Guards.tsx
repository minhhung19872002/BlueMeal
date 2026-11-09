import { type ReactNode, useEffect, useState } from 'react';
import { Navigate, useLocation } from 'react-router';
import { Spin } from 'antd';
import { useAuth } from '@/shared/auth/authStore';
import { refreshSession } from '@/shared/api/http';
import { ForbiddenPage } from '@/app/pages/ErrorPages';

/** Restores the session from the refresh cookie once per page load (access token is memory-only). */
export function useBootstrapSession() {
  const initialized = useAuth((s) => s.initialized);
  const [, force] = useState(0);
  useEffect(() => {
    if (initialized) return;
    void refreshSession().finally(() => {
      useAuth.getState().markInitialized();
      force((x) => x + 1);
    });
  }, [initialized]);
  return initialized;
}

export function FullPageSpin() {
  return (
    <div style={{ minHeight: '100vh', display: 'grid', placeItems: 'center' }}>
      <Spin size="large" />
    </div>
  );
}

/** Requires an authenticated, unrestricted session; restricted sessions go to first-login setup. */
export function RequireAuth({ children }: { children: ReactNode }) {
  const ready = useBootstrapSession();
  const token = useAuth((s) => s.accessToken);
  const restricted = useAuth((s) => s.restricted);
  const loc = useLocation();
  if (!ready) return <FullPageSpin />;
  if (!token) return <Navigate to={'/login?next=' + encodeURIComponent(loc.pathname + loc.search)} replace />;
  if (restricted) return <Navigate to="/first-login" replace />;
  return <>{children}</>;
}

/** Shows the friendly 403 page when the user lacks every listed permission (server enforces too). */
export function RequirePermission({ any, children }: { any: string[]; children: ReactNode }) {
  const user = useAuth((s) => s.user);
  if (any.length > 0 && !any.some((p) => user?.permissions.includes(p))) return <ForbiddenPage />;
  return <>{children}</>;
}

/** Renders children only when the user has one of the permissions (for buttons, menu items…). */
export function Can({ any, children, fallback = null }: { any: string[]; children: ReactNode; fallback?: ReactNode }) {
  const user = useAuth((s) => s.user);
  return any.length === 0 || any.some((p) => user?.permissions.includes(p)) ? <>{children}</> : <>{fallback}</>;
}
