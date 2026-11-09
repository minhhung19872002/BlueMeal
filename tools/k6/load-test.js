// BlueMeal load test (SPEC §11): ramp to 1,000 virtual users and hold for 10 minutes.
// Scenario per user: log in → dashboard → weekly menu → open a recipe → search foods → export a small report.
// Thresholds: p95 of read requests < 1 s, error rate < 1 %.
//
// k6 (AGPL-3.0) is used as an internal test tool only and is not distributed with BlueMeal (ASSUMPTIONS A-36).
// Run with scripts/load-test.(sh|ps1), which seeds the load-test accounts and writes the HTML report to docs/test-reports/.
import http from 'k6/http';
import { check, group, sleep } from 'k6';
import { Rate, Trend } from 'k6/metrics';

const BASE = __ENV.BASE_URL || 'http://web:8080';
const PASSWORD = __ENV.LOADTEST_PASSWORD;
const USERS = Number(__ENV.USERS || 1000);
const VUS = Number(__ENV.VUS || 1000);
const HOLD = __ENV.HOLD || '10m';
const RAMP = __ENV.RAMP || '3m';

export const options = {
  scenarios: {
    users: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: RAMP, target: VUS },
        { duration: HOLD, target: VUS },
        { duration: '1m', target: 0 },
      ],
      gracefulRampDown: '30s',
    },
  },
  thresholds: {
    'http_req_duration{kind:read}': ['p(95)<1000'],
    http_req_failed: ['rate<0.01'],
    checks: ['rate>0.99'],
    // stop early when most logins fail (wrong password, CAPTCHA tripped…) — the run would measure nothing useful
    'http_req_failed{name:login}': [{ threshold: 'rate<0.5', abortOnFail: true, delayAbortEval: '1m' }],
    // per-endpoint sub-metrics (always-true thresholds so they appear in the summary)
    'http_req_duration{name:login}': ['max>=0'],
    'http_req_duration{name:dashboard}': ['max>=0'],
    'http_req_duration{name:notifications}': ['max>=0'],
    'http_req_duration{name:menus-published}': ['max>=0'],
    'http_req_duration{name:menus-list}': ['max>=0'],
    'http_req_duration{name:recipes-list}': ['max>=0'],
    'http_req_duration{name:recipe-detail}': ['max>=0'],
    'http_req_duration{name:foods-search}': ['max>=0'],
    'http_req_duration{name:report-export}': ['max>=0'],
  },
  summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
  noCookiesReset: true,
};

const loginDuration = new Trend('login_duration', true);
const exportDuration = new Trend('export_duration', true);
const unauthorized = new Rate('unauthorized');

// per-VU state (each VU is one user: loadtest0001 … loadtestNNNN)
let token = null;
let tokenAt = 0;
let recipeIds = [];
let loginFailures = 0;

function headers() {
  return { headers: { Authorization: `Bearer ${token}`, 'Accept-Language': 'vi', 'Content-Type': 'application/json' } };
}

function login() {
  const user = `loadtest${String(((__VU - 1) % USERS) + 1).padStart(4, '0')}`;
  const r = http.post(`${BASE}/api/v1/auth/login`, JSON.stringify({ userName: user, password: PASSWORD }), {
    headers: { 'Content-Type': 'application/json', 'Accept-Language': 'vi' }, tags: { kind: 'login', name: 'login' },
  });
  loginDuration.add(r.timings.duration);
  const ok = check(r, { 'login 200': (x) => x.status === 200, 'access token': (x) => !!x.json('accessToken') });
  token = ok ? r.json('accessToken') : null;
  tokenAt = Date.now();
  return ok;
}

function get(url, name) {
  const r = http.get(`${BASE}${url}`, Object.assign(headers(), { tags: { kind: 'read', name } }));
  unauthorized.add(r.status === 401);
  check(r, { [`${name} 200`]: (x) => x.status === 200 });
  return r;
}

function isoDate(d) { return d.toISOString().slice(0, 10); }

export default function () {
  // access tokens live 15 minutes: log in on the first iteration and again before expiry
  if (!token || Date.now() - tokenAt > 13 * 60 * 1000) {
    // a user whose login failed waits longer before trying again (exponential back-off, max 60 s) — no retry storm
    if (!login()) { loginFailures++; sleep(Math.min(60, 5 * 2 ** (loginFailures - 1)) * (0.5 + Math.random())); return; }
    loginFailures = 0;
    sleep(1);
  }
  const monday = new Date(); monday.setUTCDate(monday.getUTCDate() - ((monday.getUTCDay() + 6) % 7));
  const sunday = new Date(monday); sunday.setUTCDate(monday.getUTCDate() + 6);

  group('dashboard', () => {
    get('/api/v1/dashboard', 'dashboard');
    get('/api/v1/system/notifications?take=15', 'notifications');
  });
  sleep(2 + Math.random() * 3);

  group('weekly menu', () => {
    get(`/api/v1/menus/published?from=${isoDate(monday)}&to=${isoDate(sunday)}`, 'menus-published');
    get(`/api/v1/menus?from=${isoDate(monday)}&to=${isoDate(sunday)}&pageSize=50`, 'menus-list');
  });
  sleep(2 + Math.random() * 3);

  group('recipe', () => {
    if (recipeIds.length === 0) {
      const list = get('/api/v1/recipes?pageSize=50', 'recipes-list');
      try { recipeIds = list.json('items').map((x) => x.id); } catch (e) { recipeIds = []; }
    }
    if (recipeIds.length > 0) get(`/api/v1/recipes/${recipeIds[Math.floor(Math.random() * recipeIds.length)]}`, 'recipe-detail');
  });
  sleep(2 + Math.random() * 3);

  group('food search', () => {
    const terms = ['gao', 'thit', 'ca', 'rau', 'dau', 'trung', 'sua', 'khoai'];
    get(`/api/v1/foods?q=${terms[Math.floor(Math.random() * terms.length)]}&pageSize=20`, 'foods-search');
  });
  sleep(2 + Math.random() * 3);

  // a small Excel export about once every 5 iterations (exports are much rarer than reads in practice)
  if (Math.random() < 0.2) {
    group('report export', () => {
      const r = http.post(`${BASE}/api/v1/reports/RPT-10/export`, JSON.stringify({ from: isoDate(monday), to: isoDate(sunday) }), Object.assign(headers(), { tags: { kind: 'export', name: 'report-export' }, responseType: 'none' }));
      exportDuration.add(r.timings.duration);
      check(r, { 'export 200': (x) => x.status === 200 });
    });
    sleep(2 + Math.random() * 3);
  }
}

export function handleSummary(data) {
  const out = {};
  out[__ENV.SUMMARY_JSON || 'k6-summary.json'] = JSON.stringify(data, null, 2);
  const m = data.metrics;
  const p95 = m['http_req_duration{kind:read}'] ? m['http_req_duration{kind:read}'].values['p(95)'] : NaN;
  const failed = m.http_req_failed ? m.http_req_failed.values.rate : NaN;
  out.stdout = `\nBlueMeal load test — VUs ${VUS}, hold ${HOLD}\n  read p95: ${p95.toFixed(0)} ms (threshold < 1000)\n  error rate: ${(failed * 100).toFixed(2)} % (threshold < 1 %)\n  requests: ${m.http_reqs.values.count} (${m.http_reqs.values.rate.toFixed(1)}/s)\n`;
  return out;
}
