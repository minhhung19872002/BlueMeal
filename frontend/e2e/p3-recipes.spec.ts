import { expect, test } from '@playwright/test';
import { loadAdmin, loginUi, shot } from './support';

test.describe('P3 — dishes, recipes, recommendations', () => {
  test.beforeEach(async ({ page }) => {
    await loginUi(page, loadAdmin());
  });

  test('recipe editor computes nutrition and cost in realtime', async ({ page }) => {
    await page.goto('/recipes');
    await expect(page.getByRole('heading', { name: 'Công thức món ăn' })).toBeVisible();
    await page.getByText('Gà kho gừng').first().click();
    await expect(page.getByText('Cơ cấu năng lượng P:L:G')).toBeVisible();
    await expect(page.getByText('Chi phí / suất').first()).toBeVisible();
    await page.getByTestId('recipe-servings').fill('350');
    await expect(page.getByRole('columnheader', { name: 'Số lượng mua' })).toBeVisible();
    await shot(page, 'recipe-editor');
  });

  test('dish quick view shows nutrition per serving', async ({ page }) => {
    await page.goto('/catalog/dishes');
    await page.getByTestId('catalog-search').fill('com trang');
    await page.getByTestId('catalog-search').press('Enter');
    await page.getByTestId('dish-quick').first().click();
    await expect(page.getByText('Xem nhanh dinh dưỡng & chi phí')).toBeVisible();
    await shot(page, 'dish-quick');
  });

  test('recommendations list shows meal energy target', async ({ page }) => {
    await page.goto('/catalog/recommendations');
    await expect(page.getByText('Khuyến nghị bữa ca – lao động vừa')).toBeVisible();
    await shot(page, 'recommendations');
  });
});
