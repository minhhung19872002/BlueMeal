import { expect, test } from '@playwright/test';
import { loadAdmin, loginUi, shot } from './support';

test.describe('P8 — security hardening', () => {
  test('security headers are sent on the SPA and API', async ({ request }) => {
    const page = await request.get('/');
    const h = page.headers();
    expect(h['content-security-policy']).toContain("default-src 'self'");
    expect(h['x-frame-options']).toBe('DENY');
    expect(h['x-content-type-options']).toBe('nosniff');
    expect(h['cross-origin-embedder-policy']).toBe('require-corp');
    expect(h['permissions-policy']).toContain('geolocation=()');
    const api = await request.get('/api/v1/system/version');
    expect(api.headers()['x-content-type-options']).toBe('nosniff');
  });

  test('WAF blocks a SQL injection probe', async ({ request }) => {
    const r = await request.get("/api/v1/foods?q=1' OR '1'='1' UNION SELECT password FROM users--");
    expect(r.status()).toBe(403);
  });

  test('manual backup runs in the backup container and appears in the history', async ({ page }) => {
    await loginUi(page, loadAdmin());
    await page.goto('/admin/backups');
    await page.getByTestId('backup-manual').click();
    await page.getByTestId('backup-manual-ok').click();
    await expect(page.locator('.ant-tag').filter({ hasText: 'Thành công' }).first()).toBeVisible({ timeout: 90_000 });
    await expect(page.getByTestId('backup-download').first()).toBeVisible();
    await shot(page, 'backups');
    await page.getByTestId('backup-download').first().click();
    await expect(page.getByText('Xác thực lại để tải bản sao lưu')).toBeVisible();
  });
});
