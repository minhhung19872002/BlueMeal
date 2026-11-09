// Cost probe: fixed request rates per endpoint kind, to measure server CPU per request (watch `docker stats` while it runs).
// Usage (inside the load-test network): k6 run -e KIND=login|reads|export -e RATE=20 -e DURATION=40s cost-probe.js
import http from 'k6/http';
import { check } from 'k6';

const BASE = __ENV.BASE_URL || 'http://web:8080';
const PASSWORD = __ENV.LOADTEST_PASSWORD;
const USERS = Number(__ENV.USERS || 20);
const KIND = __ENV.KIND || 'reads';
const RATE = Number(__ENV.RATE || 20);

export const options = {
  scenarios: { probe: { executor: 'constant-arrival-rate', rate: RATE, timeUnit: '1s', duration: __ENV.DURATION || '40s', preAllocatedVUs: 50, maxVUs: 400 } },
  summaryTrendStats: ['avg', 'med', 'p(95)', 'max'],
};

function login(i) {
  const r = http.post(`${BASE}/api/v1/auth/login`, JSON.stringify({ userName: `loadtest${String(i).padStart(4, '0')}`, password: PASSWORD }),
    { headers: { 'Content-Type': 'application/json' }, tags: { name: 'login' } });
  check(r, { 'login 200': (x) => x.status === 200 });
  return r.status === 200 ? r.json('accessToken') : null;
}

export function setup() {
  if (KIND === 'login') return { tokens: [] };
  const tokens = [];
  for (let i = 1; i <= Math.min(USERS, 20); i++) tokens.push(login(i));
  return { tokens };
}

const READS = ['/api/v1/dashboard', '/api/v1/system/notifications?take=15', '/api/v1/recipes?pageSize=50', '/api/v1/foods?q=gao&pageSize=20', '/api/v1/menus?pageSize=50'];

export default function (data) {
  if (KIND === 'login') { login(1 + Math.floor(Math.random() * USERS)); return; }
  const h = { headers: { Authorization: `Bearer ${data.tokens[Math.floor(Math.random() * data.tokens.length)]}`, 'Content-Type': 'application/json' } };
  if (KIND === 'export') {
    const r = http.post(`${BASE}/api/v1/reports/RPT-10/export`, JSON.stringify({ from: '2026-11-02', to: '2026-11-08' }), Object.assign(h, { responseType: 'none', tags: { name: 'export' } }));
    check(r, { '200': (x) => x.status === 200 });
    return;
  }
  const url = READS[__ITER % READS.length];
  const r = http.get(`${BASE}${url}`, Object.assign(h, { tags: { name: url.split('?')[0] } }));
  check(r, { '200': (x) => x.status === 200 });
}
