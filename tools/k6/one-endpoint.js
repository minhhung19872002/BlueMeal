import http from 'k6/http';
import { check } from 'k6';
const BASE = __ENV.BASE_URL || 'http://web:8080';
export const options = { scenarios: { p: { executor: 'constant-arrival-rate', rate: Number(__ENV.RATE || 40), timeUnit: '1s', duration: '20s', preAllocatedVUs: 30, maxVUs: 200 } }, summaryTrendStats: ['avg', 'p(95)'] };
export function setup() {
  const r = http.post(`${BASE}/api/v1/auth/login`, JSON.stringify({ userName: 'loadtest0001', password: __ENV.LOADTEST_PASSWORD }), { headers: { 'Content-Type': 'application/json' } });
  return { t: r.json('accessToken') };
}
export default function (d) {
  const r = http.get(`${BASE}${__ENV.URL}`, { headers: { Authorization: `Bearer ${d.t}` } });
  check(r, { '200': (x) => x.status === 200 });
}
