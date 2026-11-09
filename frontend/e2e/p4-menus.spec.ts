import { expect, test, type Page } from '@playwright/test';
import { loadAdmin, loginUi, shot } from './support';

/** Opens an antd select by test id, types to search and picks the first option containing `text`. */
async function pickLookup(page: Page, testId: string, search: string, text = search) {
  await page.getByTestId(testId).click();
  await page.keyboard.type(search);
  const option = page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: text }).first();
  await option.click();
  await page.keyboard.press('Escape');
}

test.describe('P4 — menus: generate → edit → submit → approve → publish', () => {
  test.beforeEach(async ({ page }) => {
    await loginUi(page, loadAdmin());
  });

  test('full menu workflow', async ({ page }) => {
    // 1. automatic generation for next week (Mon–Fri), one shift
    await page.goto('/menus/auto');
    await pickLookup(page, 'gen-shifts', 'trua', 'Ca 1');
    await pickLookup(page, 'gen-rec', 'lao dong vua', 'lao động vừa');
    await page.getByTestId('gen-run').click();
    await expect(page.getByTestId('gen-day').first()).toBeVisible({ timeout: 60_000 });
    expect(await page.getByTestId('gen-day').count()).toBeGreaterThanOrEqual(5);
    await shot(page, 'menu-auto');
    await page.getByTestId('gen-save').click();
    await expect(page).toHaveURL(/\/menus$/);

    // 2. open the newest draft (list is sorted by date) and edit the serving factor
    await page.getByRole('cell', { name: 'Nháp' }).first().click();
    await expect(page.getByRole('heading', { name: /Chi tiết thực đơn TD-/ })).toBeVisible();
    await page.getByTestId('menu-edit').click();
    await expect(page.getByText('Dinh dưỡng – chi phí (realtime)')).toBeVisible();
    await page.locator('.ant-input-number-input').last().fill('1.2');
    await page.getByTestId('menu-save').click();
    await expect(page.getByRole('heading', { name: /Chi tiết thực đơn TD-/ })).toBeVisible();
    await expect(page.getByRole('cell', { name: '1,20' })).toBeVisible();
    await shot(page, 'menu-detail');

    // 3. submit (a reason is required when critical warnings remain)
    await page.getByTestId('menu-submit').click();
    const dialog = page.locator('.ant-modal:visible');
    if (await dialog.count()) {
      await page.getByTestId('menu-comment').fill('E2E: chấp nhận cảnh báo để kiểm thử');
      await page.getByTestId('menu-action-ok').click();
    }
    await expect(page.locator('.ant-tag').filter({ hasText: 'Chờ duyệt' }).first()).toBeVisible();

    // 4. approve (1 or 2 levels depending on settings)
    for (let i = 0; i < 2; i++) {
      if (!(await page.getByTestId('menu-approve').isVisible())) break;
      await page.getByTestId('menu-approve').click();
      await page.getByTestId('menu-comment').fill('OK');
      await page.getByTestId('menu-action-ok').click();
      await expect(page.locator('.ant-modal:visible')).toHaveCount(0);
    }
    await expect(page.getByTestId('menu-publish')).toBeVisible();

    // 5. publish and check the weekly published view
    await page.getByTestId('menu-publish').click();
    await expect(page.locator('.ant-tag').filter({ hasText: 'Đã công bố' }).first()).toBeVisible();
    await page.goto('/menus/published');
    await page.locator('.ant-picker').first().click();
    await page.keyboard.press('Escape');
  });

  test('plans calendar and approval queue render', async ({ page }) => {
    await page.goto('/menus/plans');
    await expect(page.getByRole('heading', { name: 'Kế hoạch thực đơn' })).toBeVisible();
    await page.goto('/menus/approval');
    await expect(page.getByRole('heading', { name: 'Phê duyệt thực đơn' })).toBeVisible();
    await page.goto('/meal-orders');
    await expect(page.getByRole('heading', { name: 'Đặt suất ăn' })).toBeVisible();
    await shot(page, 'meal-orders');
  });
});
