import { expect, test } from '@playwright/test';
import { loadAdmin, loginUi, shot } from './support';

/**
 * Screenshots for the user manuals (docs/06_HDSD_*.md). Runs last (file name), after the functional specs created data.
 * Each test only navigates and waits for the main content — no assertions on business data.
 */
test.describe('docs screenshots', () => {
  test.beforeEach(async ({ page }) => {
    await loginUi(page, loadAdmin());
  });

  const pages: [string, string, RegExp | string][] = [
    ['/menus/plans', 'menu-plans', 'Kế hoạch thực đơn'],
    ['/menus/published', 'menu-published', 'Thực đơn công bố'],
    ['/menus/approval', 'menu-approval', 'Phê duyệt thực đơn'],
    ['/menus/new', 'menu-builder', 'Tạo thực đơn thủ công'],
    ['/profile', 'profile-mfa', /Hồ sơ|Bảo mật/],
    ['/admin/settings', 'settings', 'Cấu hình hệ thống'],
    ['/stock/issues', 'stock-issue', 'Phiếu xuất kho'],
    ['/stock/purchase-requests', 'purchase-requests', 'Đề xuất mua hàng'],
    ['/stock/locks', 'period-locks', 'Khóa sổ kho'],
    ['/catalog/customers', 'customers', /Khách hàng/],
    ['/reports', 'reports', 'Báo cáo'],
  ];

  for (const [path, name, heading] of pages) {
    test(`screenshot ${name}`, async ({ page }) => {
      await page.goto(path);
      // most pages have a heading; profile and settings use the antd Card title
      await expect(page.getByRole('heading', { name: heading }).or(page.locator('.ant-card-head-title', { hasText: heading })).first()).toBeVisible();
      await page.waitForLoadState('networkidle');
      await shot(page, name);
    });
  }

  test('screenshot menu-calendar (first plan)', async ({ page }) => {
    await page.goto('/menus/plans');
    const first = page.locator('tr.ant-table-row a').first();
    if (await first.count()) {
      await first.click();
      await expect(page.getByRole('heading', { name: /Lịch thực đơn/ })).toBeVisible();
    }
    await page.waitForLoadState('networkidle');
    await shot(page, 'menu-calendar');
  });
});
