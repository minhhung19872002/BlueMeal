import { expect, test } from '@playwright/test';
import { loadAdmin, loginUi, shot } from './support';

test.describe('P6 — analysis, alerts, cost sheet, reports, dashboard', () => {
  test.beforeEach(async ({ page }) => {
    await loginUi(page, loadAdmin());
  });

  test('dashboard shows role widgets', async ({ page }) => {
    await page.goto('/');
    await expect(page.getByTestId('dash-pendingApproval')).toBeVisible();
    await expect(page.getByTestId('dash-criticalAlerts')).toBeVisible();
    await expect(page.getByTestId('dash-expiringLots')).toBeVisible();
    await shot(page, 'dashboard');
  });

  test('stock movement report is viewed and exported to Excel', async ({ page }) => {
    await page.goto('/reports?code=RPT-02');
    await expect(page.getByRole('heading', { name: /RPT-02/ })).toBeVisible();
    await page.getByTestId('report-run').click();
    await expect(page.getByTestId('report-table')).toBeVisible();
    await shot(page, 'report-rpt02');
    const download = page.waitForEvent('download');
    await page.getByTestId('report-export').click();
    expect((await download).suggestedFilename()).toMatch(/^RPT-02_.*\.xlsx$/);
  });

  test('nutrition analysis of a date range', async ({ page }) => {
    await page.goto('/analysis/nutrition');
    await page.getByTestId('analysis-run').click();
    await expect(page.getByText(/Năng lượng|Không có thực đơn đã duyệt/).first()).toBeVisible();
    await shot(page, 'nutrition-analysis');
  });

  test('alert center runs daily checks and cost sheet renders', async ({ page }) => {
    await page.goto('/alerts');
    await page.getByTestId('alerts-run').click();
    await expect(page.locator('.ant-message').getByText(/Tạo mới \d+/)).toBeVisible();
    await shot(page, 'alerts');
    await page.goto('/cost-sheet');
    await expect(page.getByTestId('cost-table')).toBeVisible();
    await shot(page, 'cost-sheet');
  });
});
