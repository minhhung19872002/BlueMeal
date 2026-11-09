import axios from 'axios';
import { http, readCookie, CSRF_HEADER } from '@/shared/api/http';
import { useAuth, type AuthUser, type LoginResponse } from '@/shared/auth/authStore';
import { changeLanguage } from '@/shared/i18n';
import { useUi } from '@/shared/hooks/useUiStore';
import { queryClient } from '@/app/providers';

export async function login(userName: string, password: string, captchaId?: string, captchaAnswer?: string) {
  return (await http.post<LoginResponse>('/auth/login', { userName, password, captchaId, captchaAnswer })).data;
}

export async function verifyMfa(mfaToken: string, code: string, method: string) {
  return (await http.post<LoginResponse>('/auth/mfa/verify', { mfaToken, code, method })).data;
}

export async function sendEmailOtp(mfaToken: string) {
  await http.post('/auth/mfa/email-otp', { mfaToken, code: '', method: 'email' });
}

export async function captchaStatus(userName: string) {
  return (await http.get<{ required: boolean }>('/auth/captcha-status', { params: { userName } })).data.required;
}

export async function newCaptcha() {
  return (await http.get<{ id: string; svg: string }>('/auth/captcha')).data;
}

export async function fetchMe() {
  return (await http.get<AuthUser>('/auth/me')).data;
}

/** Applies a successful login: memory token + user preferences (language, theme). */
export function applySession(r: LoginResponse) {
  useAuth.getState().setSession(r);
  if (r.user) {
    void changeLanguage(r.user.language === 'en' ? 'en' : 'vi');
    useUi.getState().setTheme(r.user.theme === 'dark' ? 'dark' : 'light');
  }
}

export async function logout() {
  try {
    await axios.post('/api/v1/auth/logout', null, {
      withCredentials: true,
      headers: { [CSRF_HEADER]: readCookie('bm_csrf') ?? '', Authorization: 'Bearer ' + (useAuth.getState().accessToken ?? '') },
    });
  } catch {
    /* ignore network errors on logout */
  }
  useAuth.getState().clear();
  queryClient.clear();
}

/** Re-issues tokens after password change / MFA setup so the scope becomes "full". */
export async function refreshAfterSetup() {
  const r = await axios.post<LoginResponse>('/api/v1/auth/refresh', null, { withCredentials: true, headers: { [CSRF_HEADER]: readCookie('bm_csrf') ?? '' } });
  applySession(r.data);
  return r.data;
}
