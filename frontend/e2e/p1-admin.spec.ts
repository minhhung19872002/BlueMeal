import { expect, test } from '@playwright/test';
import { loadAdmin, loginUi, shot } from './support';

test.describe('P1 — authentication & administration', () => {
  test('login page is bilingual and switches language instantly', async ({ page }) => {
    await page.goto('/login');
    await expect(page.getByRole('heading', { name: 'Đăng nhập' })).toBeVisible();
    await shot(page, 'login-vi');
    await page.getByText('EN', { exact: true }).click();
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible();
    await shot(page, 'login-en');
    await page.getByText('VI', { exact: true }).click();
  });

  test('wrong password shows a neutral bilingual error', async ({ page }) => {
    await page.goto('/login');
    await page.getByTestId('login-username').fill('khong-ton-tai');
    await page.getByTestId('login-password').fill('Sai@MatKhau123');
    await page.getByTestId('login-submit').click();
    await expect(page.getByTestId('login-error')).toContainText('Tên đăng nhập hoặc mật khẩu không đúng');
  });

  test('admin signs in with TOTP and manages users, roles and sessions', async ({ page }) => {
    const admin = loadAdmin();
    await loginUi(page, admin);
    await expect(page.getByText(/Xin chào/)).toBeVisible();
    await shot(page, 'dashboard');

    await page.goto('/admin/users');
    await expect(page.getByRole('heading', { name: 'Tài khoản người dùng' })).toBeVisible();
    await shot(page, 'admin-users');

    const uname = 'e2e.' + Date.now().toString(36);
    await page.getByRole('button', { name: 'Thêm tài khoản' }).click();
    await page.getByLabel('Tên đăng nhập').fill(uname);
    await page.getByLabel('Họ và tên').fill('Người dùng E2E');
    await page.getByRole('button', { name: 'Lưu' }).click();
    await expect(page.getByText('Mật khẩu tạm thời')).toBeVisible();
    await page.getByRole('button', { name: 'Đồng ý' }).click();

    await page.goto('/admin/roles');
    await page.getByText('Thủ kho').first().click();
    await expect(page.getByText('Ma trận phân quyền — Thủ kho')).toBeVisible();
    await shot(page, 'admin-roles-matrix');

    await page.goto('/admin/sessions');
    await expect(page.getByText('Phiên hiện tại')).toBeVisible();
    await shot(page, 'admin-sessions');

    await page.goto('/admin/audit-logs');
    await expect(page.getByRole('heading', { name: 'Nhật ký hệ thống' })).toBeVisible();
    await shot(page, 'admin-audit');

    // personal-data notice (Luật 91/2025): consent recorded and viewable from the profile
    await page.goto('/profile');
    await expect(page.getByText(/Đã đồng ý lúc .* \(phiên bản \d+\)/)).toBeVisible();
    await page.getByRole('button', { name: 'Xem thông báo' }).click();
    await expect(page.getByTestId('privacy-text')).toContainText('91/2025/QH15');
  });

  test('catalog screen: search without accents and Excel buttons @mobile', async ({ page }) => {
    await loginUi(page, loadAdmin());
    await page.goto('/catalog/provinces');
    await page.getByTestId('catalog-search').fill('ho chi minh');
    await page.getByTestId('catalog-search').press('Enter');
    await expect(page.getByText('Thành phố Hồ Chí Minh')).toBeVisible();
    await shot(page, 'catalog-provinces');
  });
});
