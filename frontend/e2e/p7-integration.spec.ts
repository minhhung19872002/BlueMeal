import { expect, test } from '@playwright/test';
import { loadAdmin, loginUi, shot } from './support';

test.describe('P7 — VDD integration', () => {
  test.beforeEach(async ({ page }) => {
    await loginUi(page, loadAdmin());
  });

  test('sync from the mock portal and resolve the conflict', async ({ page }) => {
    await page.goto('/admin/integration');
    await expect(page.getByText('VDD-MOCK').first()).toBeVisible();
    await page.getByTestId('ep-test').first().click();
    await expect(page.locator('.ant-message').getByText(/Kết nối tốt/)).toBeVisible();
    await page.getByTestId('ep-sync').first().click();
    await expect(page.locator('.ant-message').getByText(/Nhận \d+/)).toBeVisible({ timeout: 60_000 });
    await shot(page, 'integration-endpoints');

    await page.getByTestId('tab-conflicts').click();
    const card = page.getByTestId('conflict-card').filter({ hasText: 'M-NC01' });
    if (await card.count()) {
      await expect(card.first().getByText('Đang dùng (đã sửa tay)')).toBeVisible();
      await shot(page, 'integration-conflicts');
      await card.first().getByTestId('conflict-keep-local').click();
      await expect(page.getByTestId('conflict-card').filter({ hasText: 'M-NC01' })).toHaveCount(0);
    }

    await page.getByRole('tab', { name: 'Lịch sử đồng bộ' }).click();
    await expect(page.getByRole('cell', { name: /Sync/ }).first()).toBeVisible();
    await shot(page, 'integration-logs');

    await page.goto('/catalog/foods');
    await page.getByTestId('catalog-search').fill('VDD-1201');
    await page.getByTestId('catalog-search').press('Enter');
    await expect(page.getByRole('cell', { name: 'Nước mắm' }).first()).toBeVisible();
  });
});
