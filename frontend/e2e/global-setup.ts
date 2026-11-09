import { BASE_URL, hasAdminState, saveAdmin, totp } from './support';

/**
 * Prepares the admin account for E2E runs. On a fresh database the seeded admin must change its password and
 * enroll TOTP (first-login policy); we do that once through the API and store the secret in e2e/.auth/admin.json
 * (git-ignored). Provide E2E_ADMIN_PASSWORD + E2E_ADMIN_TOTP to reuse an existing configured admin instead.
 */
export default async function globalSetup() {
  if (hasAdminState()) return;
  const initial = process.env.E2E_ADMIN_INITIAL_PASSWORD ?? 'BlueMeal@2026!';
  const newPassword = process.env.E2E_ADMIN_NEW_PASSWORD ?? 'E2e@BlueMeal2026!';
  const login = await (await fetch(BASE_URL + '/api/v1/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ userName: 'admin', password: initial }) })).json();
  if (!login.accessToken) throw new Error('Cannot log in as seeded admin; set E2E_ADMIN_PASSWORD and E2E_ADMIN_TOTP. Response: ' + JSON.stringify(login));
  const auth = { Authorization: 'Bearer ' + login.accessToken, 'Content-Type': 'application/json' };
  const pw = await fetch(BASE_URL + '/api/v1/profile/change-password', { method: 'POST', headers: auth, body: JSON.stringify({ currentPassword: initial, newPassword }) });
  if (!pw.ok) throw new Error('change-password failed: ' + (await pw.text()));
  const setup = await (await fetch(BASE_URL + '/api/v1/profile/mfa/setup', { method: 'POST', headers: auth })).json();
  const en = await fetch(BASE_URL + '/api/v1/profile/mfa/enable', { method: 'POST', headers: auth, body: JSON.stringify({ code: totp(setup.secret) }) });
  if (!en.ok) throw new Error('mfa enable failed: ' + (await en.text()));
  saveAdmin({ userName: 'admin', password: newPassword, totpSecret: setup.secret });
}
