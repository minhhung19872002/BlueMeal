import { expect, test, type Page } from '@playwright/test';
import { loadAdmin, loginUi, shot } from './support';

/**
 * SPEC §9: stock, inspections, samples, published menu and menu approval must work on phones (≥ 360 px).
 * Runs in the "mobile" project (Pixel 7) through the @mobile tag; also passes on desktop.
 */
async function noHorizontalScroll(page: Page) {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(overflow).toBeLessThanOrEqual(1);
}

test.describe('P9 — responsive screens @mobile', () => {
  test.beforeEach(async ({ page }) => {
    await loginUi(page, loadAdmin());
  });

  const screens: [string, string, RegExp | string][] = [
    ['/stock/receipts', 'mobile-stock-receipts', 'Phiếu nhập kho'],
    ['/stock/takes', 'mobile-stock-takes', 'Kiểm kê kho'],
    ['/stock/balance', 'mobile-stock-balance', 'Tồn kho'],
    ['/safety/inspections', 'mobile-inspections', 'Kiểm thực 3 bước'],
    ['/safety/samples', 'mobile-samples', 'Sổ lưu mẫu thức ăn'],
    ['/menus/published', 'mobile-menu-published', 'Thực đơn công bố'],
    ['/menus/approval', 'mobile-menu-approval', 'Phê duyệt thực đơn'],
  ];

  for (const [path, name, heading] of screens) {
    test(`${path} fits the screen`, async ({ page }, info) => {
      await page.goto(path);
      await expect(page.getByRole('heading', { name: heading }).first()).toBeVisible();
      await page.waitForLoadState('networkidle');
      // tables scroll inside their own container; the page itself must not scroll sideways
      await noHorizontalScroll(page);
      if (info.project.name === 'mobile') await shot(page, name);
    });
  }

  test('sample form is usable on a phone', async ({ page }) => {
    await page.goto('/safety/samples');
    await page.getByTestId('sample-add').click();
    await expect(page.getByTestId('sample-save')).toBeVisible();
    await expect(page.getByTestId('sample-dish')).toBeVisible();
    await noHorizontalScroll(page);
  });
});
