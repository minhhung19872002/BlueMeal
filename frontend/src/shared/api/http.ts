import axios, { AxiosError, type AxiosRequestConfig, type InternalAxiosRequestConfig } from 'axios';
import i18n from '@/shared/i18n';
import { useAuth, type LoginResponse } from '@/shared/auth/authStore';

export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
}

export const CSRF_HEADER = 'X-BlueMeal-CSRF';

export function readCookie(name: string): string | null {
  const m = document.cookie.split('; ').find((c) => c.startsWith(name + '='));
  return m ? decodeURIComponent(m.slice(name.length + 1)) : null;
}

export const http = axios.create({ baseURL: '/api/v1', withCredentials: true, timeout: 120_000 });

http.interceptors.request.use((cfg: InternalAxiosRequestConfig) => {
  const token = useAuth.getState().accessToken;
  if (token) cfg.headers.set('Authorization', 'Bearer ' + token);
  cfg.headers.set('Accept-Language', i18n.language === 'en' ? 'en' : 'vi');
  const csrf = readCookie('bm_csrf');
  if (csrf) cfg.headers.set(CSRF_HEADER, csrf);
  return cfg;
});

let refreshing: Promise<boolean> | null = null;

/** Rotates the refresh token (HttpOnly cookie) and stores the new access token in memory. */
export async function refreshSession(): Promise<boolean> {
  if (!refreshing) {
    refreshing = axios
      .post<LoginResponse>('/api/v1/auth/refresh', null, {
        withCredentials: true,
        headers: { [CSRF_HEADER]: readCookie('bm_csrf') ?? '', 'Accept-Language': i18n.language },
      })
      .then((r) => {
        useAuth.getState().setSession(r.data);
        return true;
      })
      .catch(() => {
        useAuth.getState().clear();
        return false;
      })
      .finally(() => {
        setTimeout(() => {
          refreshing = null;
        }, 0);
      });
  }
  return refreshing;
}

type RetryConfig = AxiosRequestConfig & { _retried?: boolean };

http.interceptors.response.use(
  (r) => r,
  async (error: AxiosError<ProblemDetails>) => {
    const cfg = error.config as RetryConfig | undefined;
    const status = error.response?.status;
    const url = cfg?.url ?? '';
    if (status === 401 && cfg && !cfg._retried && !url.startsWith('/auth/')) {
      cfg._retried = true;
      if (await refreshSession()) return http(cfg);
      if (!location.pathname.startsWith('/login')) location.assign('/login?expired=1');
    }
    if (status === 503 && error.response?.data?.code === 'maintenance' && location.pathname !== '/maintenance') {
      location.assign('/maintenance');
    }
    return Promise.reject(error);
  },
);

/** Human-readable (server-localized) error message from an axios error. */
export function problemMessage(err: unknown, fallback?: string): string {
  const e = err as AxiosError<ProblemDetails>;
  const p = e?.response?.data;
  if (p?.errors) {
    const first = Object.values(p.errors)[0]?.[0];
    if (first) return first;
  }
  return p?.detail ?? p?.title ?? fallback ?? i18n.t('common:error.generic');
}

export function problemFieldErrors(err: unknown): Record<string, string> {
  const p = (err as AxiosError<ProblemDetails>)?.response?.data;
  const out: Record<string, string> = {};
  if (p?.errors) for (const [k, v] of Object.entries(p.errors)) out[k] = v[0];
  return out;
}

export function problemCode(err: unknown): string | undefined {
  return (err as AxiosError<ProblemDetails>)?.response?.data?.code;
}

/** Downloads a binary response (Excel export etc.) using the server-provided file name. */
export async function downloadFile(url: string, params?: Record<string, unknown>, fallbackName = 'export.xlsx') {
  const r = await http.get(url, { params, responseType: 'blob' });
  const cd = (r.headers['content-disposition'] as string | undefined) ?? '';
  const m = /filename\*=UTF-8''([^;]+)|filename="?([^";]+)"?/i.exec(cd);
  const name = m ? decodeURIComponent(m[1] ?? m[2]) : fallbackName;
  const href = URL.createObjectURL(r.data as Blob);
  const a = document.createElement('a');
  a.href = href;
  a.download = name;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(href), 1000);
}
