import { createHmac } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Page } from '@playwright/test';

export const BASE_URL = process.env.BASE_URL ?? 'http://localhost:8080';
const here = dirname(fileURLToPath(import.meta.url));
export const STATE_FILE = join(here, '.auth', 'admin.json');
export const SCREENSHOT_DIR = join(here, '..', '..', 'docs', 'images');

export interface AdminState { userName: string; password: string; totpSecret: string }

/** RFC 6238 TOTP (SHA-1, 30 s, 6 digits) — same as Google/Microsoft Authenticator. */
export function totp(secretBase32: string, at = Date.now()): string {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = '';
  for (const c of secretBase32.replace(/=+$/, '').toUpperCase()) bits += alphabet.indexOf(c).toString(2).padStart(5, '0');
  const bytes = Buffer.from(bits.match(/.{8}/g)!.map((b) => parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(at / 1000 / 30)));
  const h = createHmac('sha1', bytes).update(counter).digest();
  const o = h[h.length - 1] & 0xf;
  const code = ((h.readUInt32BE(o) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0');
  return code;
}

export function loadAdmin(): AdminState {
  if (process.env.E2E_ADMIN_PASSWORD && process.env.E2E_ADMIN_TOTP)
    return { userName: process.env.E2E_ADMIN_USER ?? 'admin', password: process.env.E2E_ADMIN_PASSWORD, totpSecret: process.env.E2E_ADMIN_TOTP };
  return JSON.parse(readFileSync(STATE_FILE, 'utf8')) as AdminState;
}

export function saveAdmin(s: AdminState) {
  mkdirSync(dirname(STATE_FILE), { recursive: true });
  writeFileSync(STATE_FILE, JSON.stringify(s, null, 2));
}

export const hasAdminState = () => existsSync(STATE_FILE) || !!process.env.E2E_ADMIN_TOTP;

const USED_FILE = join(here, '.auth', 'totp-used.json');

function usedCodes(): string[] {
  try {
    return JSON.parse(readFileSync(USED_FILE, 'utf8')) as string[];
  } catch {
    return [];
  }
}

/**
 * Returns a TOTP code not used before (the server rejects replays — SPEC §10.1), waiting for the next 30 s window
 * when needed. Used codes are shared across Playwright workers/projects through a small file.
 */
export async function freshTotp(secret: string): Promise<string> {
  let code = totp(secret);
  while (usedCodes().includes(code)) {
    await new Promise((r) => setTimeout(r, 1000));
    code = totp(secret);
  }
  mkdirSync(dirname(USED_FILE), { recursive: true });
  writeFileSync(USED_FILE, JSON.stringify([...usedCodes().slice(-10), code]));
  return code;
}

/** UI login including the TOTP step. */
export async function loginUi(page: Page, user: AdminState) {
  await page.goto('/login');
  await page.getByTestId('login-username').fill(user.userName);
  await page.getByTestId('login-password').fill(user.password);
  await page.getByTestId('login-submit').click();
  const mfa = page.getByTestId('mfa-code');
  await mfa.waitFor({ timeout: 15_000 });
  await mfa.fill(await freshTotp(user.totpSecret));
  await mfa.press('Enter');
  await page.waitForURL((u) => !u.pathname.startsWith('/login'));
}

export async function shot(page: Page, name: string) {
  mkdirSync(SCREENSHOT_DIR, { recursive: true });
  await page.screenshot({ path: join(SCREENSHOT_DIR, name + '.png'), fullPage: false });
}

/** Minimal API client using the same session cookies as the browser is not needed; we use bearer tokens. */
export async function apiLogin(user: AdminState): Promise<string> {
  const r1 = await fetch(BASE_URL + '/api/v1/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userName: user.userName, password: user.password }) });
  const b1: any = await r1.json();
  if (!b1.mfaRequired) return b1.accessToken;
  const r2 = await fetch(BASE_URL + '/api/v1/auth/mfa/verify', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ mfaToken: b1.mfaToken, code: await freshTotp(user.totpSecret), method: 'totp' }) });
  const b2: any = await r2.json();
  if (!b2.accessToken) throw new Error('API login failed: ' + JSON.stringify(b2));
  return b2.accessToken;
}

export async function api<T = any>(token: string, method: string, path: string, body?: unknown): Promise<T> {
  const r = await fetch(BASE_URL + '/api/v1' + path, { method, headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' }, body: body === undefined ? undefined : JSON.stringify(body) });
  if (!r.ok) throw new Error(`${method} ${path} → ${r.status} ${await r.text()}`);
  const text = await r.text();
  return (text ? JSON.parse(text) : undefined) as T;
}
