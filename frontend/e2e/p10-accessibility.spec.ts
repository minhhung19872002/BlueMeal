import AxeBuilder from '@axe-core/playwright';
import { expect, test, type Page } from '@playwright/test';
import { loadAdmin, loginUi } from './support';

/**
 * Accessibility (TT 39/2017/TT-BTTTT, Phụ lục — WCAG 2.0 mức AA): automated axe-core scan of the main screens.
 * Fails on "serious" or "critical" violations of the WCAG 2.0/2.1 A + AA rules; minor/moderate ones are listed in the report.
 * Automated checks cover roughly a third of WCAG — keyboard and screen-reader walkthroughs stay in the manual checklist.
 */
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa'];

async function scan(page: Page, name: string) {
  await page.waitForLoadState('networkidle');
  // .ant-table-measure-row: Ant Design's zero-height, aria-hidden row used only to measure fixed-layout column widths
  const result = await new AxeBuilder({ page }).withTags(TAGS).exclude('.ant-table-measure-row').analyze();
  const blocking = result.violations.filter((v) => v.impact === 'serious' || v.impact === 'critical');
  const summary = (list: typeof result.violations) =>
    list.map((v) => `[${v.impact}] ${v.id}: ${v.help} (${v.nodes.length}×)\n${v.nodes.map((n) => `    ${n.target.join(' ')} — ${n.any[0]?.message ?? n.failureSummary ?? ''}`).join('\n')}`).join('\n');
  if (result.violations.length) await test.info().attach(`axe-${name}.txt`, { body: summary(result.violations), contentType: 'text/plain' });
  expect(summary(blocking), `serious/critical WCAG violations on ${name}`).toBe('');
}

test.describe('P10 — accessibility (WCAG 2.0/2.1 AA, axe-core)', () => {
  test('login page', async ({ page }) => {
    await page.goto('/login');
    await expect(page.getByTestId('login-username')).toBeVisible();
    await scan(page, 'login');
  });

  test.describe('signed in', () => {
    // one sign-in for all screens (each login waits for a fresh TOTP window)
    test.describe.configure({ mode: 'serial' });
    let page: Page;
    test.beforeAll(async ({ browser }, info) => {
      // axe needs a page from an explicit context; carry over the project's base URL, locale and viewport
      const { baseURL, locale, timezoneId, viewport, ignoreHTTPSErrors } = info.project.use;
      page = await (await browser.newContext({ baseURL, locale, timezoneId, viewport, ignoreHTTPSErrors })).newPage();
      await loginUi(page, loadAdmin());
    });
    test.afterAll(async () => {
      await page.context().close();
    });

    const screens: [string, string][] = [
      ['/', 'dashboard'],
      ['/menus', 'menus'],
      ['/menus/published', 'menus-published'],
      ['/analysis/nutrition', 'nutrition-analysis'],
      ['/catalog/foods', 'foods'],
      ['/catalog/provinces', 'provinces'],
      ['/recipes', 'recipes'],
      ['/stock/receipts', 'stock-receipts'],
      ['/stock/balance', 'stock-balance'],
      ['/safety/samples', 'samples'],
      ['/reports', 'reports'],
      ['/admin/users', 'users'],
      ['/admin/settings', 'settings'],
    ];

    for (const [path, name] of screens) {
      test(`${path}`, async () => {
        await page.goto(path);
        await expect(page.locator('main, [role="main"]').first()).toBeVisible();
        await scan(page, name);
      });
    }
  });
});
