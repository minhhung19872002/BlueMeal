// Lighthouse audit of the main screens (SPEC §11: score ≥ 85). Usage: npm run lighthouse  (stack running on BASE_URL)
// Logs in with Playwright in a persistent Chromium profile, then runs Lighthouse (desktop preset) on each page through the
// same browser so the refresh-token cookie restores the session. Reports: docs/test-reports/lighthouse/.
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';
import lighthouse from 'lighthouse';
import desktopConfig from 'lighthouse/core/config/desktop-config.js';
import { freshTotp, loadAdmin } from '../e2e/support.ts';

const here = dirname(fileURLToPath(import.meta.url));
const OUT = join(here, '..', '..', 'docs', 'test-reports', 'lighthouse');
const BASE = process.env.BASE_URL ?? 'http://localhost:8080';
const PORT = 9333;
const PAGES = [
  ['login', '/login', false],
  ['dashboard', '/', true],
  ['menus', '/menus', true],
  ['menu-published', '/menus/published', true],
  ['recipes', '/recipes', true],
  ['foods', '/catalog/foods', true],
  ['stock-balance', '/stock/balance', true],
  ['reports', '/reports', true],
];
const CATEGORIES = ['performance', 'accessibility', 'best-practices'];

mkdirSync(OUT, { recursive: true });
const ctx = await chromium.launchPersistentContext(join(OUT, '.profile'), { headless: true, args: [`--remote-debugging-port=${PORT}`] });
const page = await ctx.newPage();
const admin = loadAdmin();
await page.goto(`${BASE}/login`);
await page.getByTestId('login-username').fill(admin.userName);
await page.getByTestId('login-password').fill(admin.password);
await page.getByTestId('login-submit').click();
await page.getByTestId('mfa-code').fill(await freshTotp(admin.totpSecret));
await page.getByTestId('mfa-code').press('Enter');
await page.waitForURL((u) => !u.pathname.startsWith('/login'));

const rows = [];
for (const [name, path, auth] of PAGES) {
  const result = await lighthouse(`${BASE}${path}`, { port: PORT, output: 'html', logLevel: 'error', disableStorageReset: auth, onlyCategories: CATEGORIES }, desktopConfig);
  writeFileSync(join(OUT, `${name}.html`), result.report);
  const scores = Object.fromEntries(CATEGORIES.map((c) => [c, Math.round((result.lhr.categories[c]?.score ?? 0) * 100)]));
  rows.push({ name, path, ...scores, finalUrl: result.lhr.finalDisplayedUrl });
  console.log(name.padEnd(16), CATEGORIES.map((c) => `${c} ${scores[c]}`).join('  '));
}
await ctx.close();

const failing = rows.filter((r) => CATEGORIES.some((c) => r[c] < 85));
const md = [
  '# Lighthouse — BlueMeal',
  '',
  `Đo ngày ${new Date().toISOString().slice(0, 10)} trên ${BASE} (preset desktop, Chromium headless; stack Docker dev qua nginx + WAF). Ngưỡng SPEC §11: ≥ 85.`,
  '',
  '| Trang | Đường dẫn | Performance | Accessibility | Best practices |',
  '|---|---|---|---|---|',
  ...rows.map((r) => `| ${r.name} | \`${r.path}\` | ${r.performance} | ${r.accessibility} | ${r['best-practices']} |`),
  '',
  failing.length ? `Trang dưới ngưỡng: ${failing.map((r) => r.name).join(', ')}` : 'Tất cả trang đạt ngưỡng ≥ 85.',
  '',
  'Báo cáo chi tiết từng trang: các file `*.html` trong thư mục này.',
].join('\n');
writeFileSync(join(OUT, 'README.md'), md + '\n');
writeFileSync(join(OUT, 'scores.json'), JSON.stringify(rows, null, 2));
if (failing.length) process.exitCode = 1;
