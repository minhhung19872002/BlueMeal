import { expect, test } from '@playwright/test';
import { loadAdmin, loginUi, shot } from './support';

test.describe('P2 — base catalogs', () => {
  test.beforeEach(async ({ page }) => {
    await loginUi(page, loadAdmin());
  });

  test('foods list shows sample-data label and nutrient editor', async ({ page }) => {
    await page.goto('/catalog/foods');
    await page.getByTestId('catalog-search').fill('gao te');
    await page.getByTestId('catalog-search').press('Enter');
    await expect(page.getByText('Gạo tẻ máy')).toBeVisible();
    await expect(page.getByText('[DỮ LIỆU MẪU]').first()).toBeVisible();
    await shot(page, 'catalog-foods');
    await page.getByText('Gạo tẻ máy').dblclick();
    await expect(page.getByTestId('nutrient-ENERGY')).toBeVisible();
    await shot(page, 'catalog-food-edit');
  });

  test('nutrient grid supports quick inline editing', async ({ page }) => {
    await page.goto('/catalog/foods');
    await page.getByText('Lưới dinh dưỡng /100g').click();
    await page.getByTestId('grid-search').fill('ca chua');
    await page.getByTestId('grid-search').press('Enter');
    const cell = page.getByRole('row', { name: /Cà chua/ }).getByLabel('VIT_C');
    await expect(cell).toBeVisible();
    await cell.fill('40,5');
    await cell.press('Enter');
    await page.waitForTimeout(500);
    await shot(page, 'catalog-food-grid');
  });

  test('partner detail tabs: contracts and food safety certificates', async ({ page }) => {
    await page.goto('/catalog/partners');
    await page.getByText(/Thủy sản Bắc Bộ/).dblclick();
    await page.getByRole('tab', { name: 'Giấy chứng nhận ATTP' }).click();
    await expect(page.getByText('Đã hết hạn')).toBeVisible();
    await shot(page, 'catalog-partner-certificates');
  });

  test('ingredients show mapping to food composition and stock @mobile', async ({ page }) => {
    await page.goto('/catalog/ingredients');
    await expect(page.getByRole('heading', { name: 'Nguyên liệu' })).toBeVisible();
    await shot(page, 'catalog-ingredients');
  });
});
