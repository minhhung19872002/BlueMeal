import { expect, test, type Page } from '@playwright/test';
import { loadAdmin, loginUi, shot } from './support';

async function pickLookup(page: Page, testId: string, search: string, text = search) {
  await page.getByTestId(testId).first().click();
  await page.keyboard.type(search);
  await page.locator('.ant-select-dropdown:visible .ant-select-item-option').filter({ hasText: text }).first().click();
}

test.describe('P5 — stock & food safety', () => {
  test.beforeEach(async ({ page }) => {
    await loginUi(page, loadAdmin());
  });

  test('receipt with quality check is posted and shows in balance and stock card', async ({ page }) => {
    await page.goto('/stock/receipts/new');
    await pickLookup(page, 'receipt-warehouse', 'kho kho', 'Kho khô');
    await pickLookup(page, 'receipt-ingredient', 'gao te', 'Gạo tẻ');
    await page.getByTestId('receipt-qty').fill('25');
    await page.getByTestId('receipt-price').fill('18000');
    await page.getByTestId('receipt-post').click();
    await page.getByTestId('confirm-post').click();
    await expect(page.locator('.ant-tag').filter({ hasText: 'Đã ghi sổ' }).first()).toBeVisible();
    await expect(page.getByRole('heading', { name: /Phiếu nhập PN-/ })).toBeVisible();
    await shot(page, 'stock-receipt');

    await page.goto('/stock/balance');
    await page.getByPlaceholder('Nguyên liệu').fill('gao te');
    await page.getByPlaceholder('Nguyên liệu').press('Enter');
    await expect(page.getByRole('cell', { name: /Gạo tẻ/ }).first()).toBeVisible();
    await shot(page, 'stock-balance');
    await page.locator('tr.ant-table-row').first().getByText('Thẻ kho').click();
    await expect(page.getByText('Tồn cuối kỳ')).toBeVisible();
    await expect(page.getByRole('link', { name: /PN-/ }).first()).toBeVisible();
    await shot(page, 'stock-card');
  });

  test('stock take: count and approve', async ({ page }) => {
    await page.goto('/stock/takes');
    await page.getByTestId('take-new').click();
    await pickLookup(page, 'take-warehouse', 'kho kho', 'Kho khô');
    await page.getByTestId('take-start').click();
    await expect(page.getByRole('heading', { name: /Kiểm kê kho KK-/ })).toBeVisible();
    const inputs = page.getByTestId('take-actual');
    const n = await inputs.count();
    expect(n).toBeGreaterThan(0);
    for (let i = 0; i < n; i++) {
      // count = book quantity, except the first line (1 unit short)
      const book = await page.locator('tr.ant-table-row').nth(i).locator('td').nth(1).innerText();
      const value = Number(book.split(' ')[0].replace(/\./g, '').replace(',', '.'));
      await inputs.nth(i).fill(String(i === 0 ? Math.max(0, value - 1) : value).replace('.', ','));
    }
    await page.getByTestId('take-save').click();
    await page.getByTestId('take-approve').click();
    await page.getByTestId('confirm-approve').click();
    await expect(page.locator('.ant-tag').filter({ hasText: 'Đã duyệt' }).first()).toBeVisible();
    await expect(page.getByText('Chứng từ điều chỉnh')).toBeVisible();
    await shot(page, 'stock-take');
  });

  test('food sample is recorded with planned disposal', async ({ page }) => {
    await page.goto('/safety/samples');
    await page.getByTestId('sample-add').click();
    await pickLookup(page, 'sample-dish', 'canh', 'Canh');
    await page.getByTestId('sample-save').click();
    await expect(page.locator('.ant-tag').filter({ hasText: 'Đang lưu' }).first()).toBeVisible();
    await shot(page, 'food-samples');
    await page.goto('/safety/inspections');
    await expect(page.getByRole('tab', { name: 'Bước 1 – Trước chế biến' })).toBeVisible();
    await shot(page, 'inspections');
  });
});
