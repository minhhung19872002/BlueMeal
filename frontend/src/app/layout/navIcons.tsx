/** SVG paths from "BlueMeal Layout.dc.html" (maps I and CI), keyed by NAV keys in app/navigation.tsx. */
const I = {
  dashboard: 'M3 3h7v9H3zM14 3h7v5h-7zM14 12h7v9h-7zM3 16h7v5H3z',
  menus: 'M8 2v4M16 2v4M3 10h18M5 4h14a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2z',
  analysis: 'M3 3v18h18M8 17v-5M13 17V7M18 17v-8',
  food: 'M9 3h6M10 3v6L4.5 19a1.5 1.5 0 0 0 1.3 2.2h12.4a1.5 1.5 0 0 0 1.3-2.2L14 9V3M7.5 15h9',
  stock: 'M22 12h-6l-2 3h-4l-2-3H2M5.5 5.1 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.5-6.9A2 2 0 0 0 16.8 4H7.2a2 2 0 0 0-1.7 1.1z',
  safety: 'M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10zM9 12l2 2 4-4',
  partners: 'M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM22 21v-2a4 4 0 0 0-3-3.9M16 3.1a4 4 0 0 1 0 7.8',
  reports: 'M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8zM14 2v6h6M8 13h8M8 17h5',
  system: 'M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6',
  audit: 'M9 11l3 3 8-8M20 12v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11',
  alert: 'M10.3 3.9 1.8 18a2 2 0 0 0 1.7 3h17a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0zM12 9v4M12 17h.01',
  wallet: 'M20 12V8H6a2 2 0 0 1 0-4h12v4M4 6v12a2 2 0 0 0 2 2h14v-4M18 12a2 2 0 0 0 0 4h4v-4z',
  fire: 'M12 22a7 7 0 0 0 7-7c0-4-3-6-4-9-2 1-3 3-3 5-1-1-2-2-2-4-3 2-5 5-5 8a7 7 0 0 0 7 7z',
  clock: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 6v6l4 2',
};

const CI = {
  list: 'M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01', cal: I.menus, bolt: 'M13 2 4 14h7l-1 8 9-12h-7z', edit: 'M12 20h9M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4z', check: I.audit,
  pub: 'M3 11v2a1 1 0 0 0 1 1h3l5 4V6L7 10H4a1 1 0 0 0-1 1zM16 9a4 4 0 0 1 0 6M19 6a8 8 0 0 1 0 12', users: I.partners, chart: I.analysis, bell: 'M18 8a6 6 0 0 0-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.7 21a2 2 0 0 1-3.4 0', wallet: I.wallet,
  leaf: 'M11 20A7 7 0 0 1 4 13c0-6 7-9 16-10-1 9-4 17-9 17zM4 21c2-5 5-8 9-10', box: 'M21 8 12 3 3 8v8l9 5 9-5zM3 8l9 5 9-5M12 13v8', bowl: 'M3 11h18a9 9 0 0 1-18 0zM8 21h8M12 3v4M8 4v3M16 4v3',
  book: 'M4 19.5A2.5 2.5 0 0 1 6.5 17H20V3H6.5A2.5 2.5 0 0 0 4 5.5zM4 19.5A2.5 2.5 0 0 0 6.5 22H20v-5', target: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 18a6 6 0 1 0 0-12 6 6 0 0 0 0 12zM12 14a2 2 0 1 0 0-4 2 2 0 0 0 0 4z',
  grid: 'M3 3h8v8H3zM13 3h8v8h-8zM3 13h8v8H3zM13 13h8v8h-8z', flask: I.food, clock: I.clock, layers: 'M12 2 2 7l10 5 10-5zM2 12l10 5 10-5M2 17l10 5 10-5', home: 'M3 10 12 3l9 7v11H3zM9 21v-7h6v7',
  folder: 'M3 6a2 2 0 0 1 2-2h4l2 3h8a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z', down: 'M12 3v12M7 10l5 5 5-5M4 21h16', up: 'M12 15V3M7 8l5-5 5 5M4 21h16',
  clip: 'M9 4h6v3H9zM9 5H6a1 1 0 0 0-1 1v14a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V6a1 1 0 0 0-1-1h-3M9 12h6M9 16h6', card: 'M3 5h18v14H3zM3 10h18M7 15h4',
  cart: 'M3 4h2l2.5 11h10L20 7H6.5M9 20a1 1 0 1 0 0-2 1 1 0 0 0 0 2zM17 20a1 1 0 1 0 0-2 1 1 0 0 0 0 2z', lock: 'M6 11h12v10H6zM8 11V7a4 4 0 0 1 8 0v4', shield: I.safety,
  bld: 'M4 21V4h10v17M14 9h6v12M2 21h20M8 8h2M8 12h2M8 16h2', tree: 'M12 3v6M5 21v-6h14v6M12 9v6M3 21h4M10 21h4M17 21h4', user: 'M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z',
  key: 'M15 7a4 4 0 1 1-3.9 5H8v3H5v3H2v-3l6.1-6.1A4 4 0 0 1 15 7z', mon: 'M3 4h18v12H3zM8 21h8M12 16v5', sl: I.system, link: 'M10 13a5 5 0 0 0 7 0l3-3a5 5 0 0 0-7-7l-1 1M14 11a5 5 0 0 0-7 0l-3 3a5 5 0 0 0 7 7l1-1',
  db: 'M4 6c0-1.7 3.6-3 8-3s8 1.3 8 3-3.6 3-8 3-8-1.3-8-3zM4 6v6c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12v6c0 1.7 3.6 3 8 3s8-1.3 8-3v-6', hist: 'M3 12a9 9 0 1 0 3-6.7L3 8M3 3v5h5M12 7v5l3 2',
  info: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 16v-4M12 8h.01', pin: 'M12 21s7-6.3 7-11a7 7 0 0 0-14 0c0 4.7 7 11 7 11zM12 12a2 2 0 1 0 0-4 2 2 0 0 0 0 4z', scale: 'M12 3v18M5 7h14M5 7l-3 7a3 3 0 0 0 6 0zM19 7l-3 7a3 3 0 0 0 6 0zM8 21h8',
};

/** Child item → CI icon, as in the design's NAV. Keys the design does not have (permissions, countries, wards) reuse the closest icon. */
const CHILD: Record<string, keyof typeof CI> = {
  'menu-plans': 'cal', 'menu-list': 'list', 'menu-auto': 'bolt', 'menu-manual': 'edit', 'menu-approval': 'check', 'menu-published': 'pub', 'meal-orders': 'users',
  'nutrition-analysis': 'chart', alerts: 'bell', 'cost-sheet': 'wallet',
  foods: 'leaf', ingredients: 'box', dishes: 'bowl', recipes: 'book', recommendations: 'target', 'food-groups': 'grid', nutrients: 'flask', 'meal-shifts': 'clock', 'meal-structures': 'layers', warehouses: 'home', 'report-groups': 'folder',
  'stock-receipts': 'down', 'stock-issues': 'up', 'stock-takes': 'clip', 'stock-balance': 'box', 'stock-card': 'card', 'purchase-requests': 'cart', 'period-locks': 'lock',
  inspections: 'check', samples: 'flask', 'food-safety': 'shield', 'safety-dashboard': 'chart',
  subjects: 'users', 'partners-list': 'bld', 'partner-types': 'grid', customers: 'user', 'customer-types': 'grid',
  organization: 'bld', departments: 'tree', employees: 'users', roles: 'key', users: 'user', sessions: 'mon', permissions: 'lock',
  countries: 'pin', provinces: 'pin', wards: 'pin', units: 'scale', settings: 'sl', integration: 'link', backups: 'db', 'audit-logs': 'hist', version: 'info',
};

export const groupIcon = (key: string): string => (I as Record<string, string>)[key] ?? CI.list;
export const childIcon = (key: string): string => CI[CHILD[key]] ?? CI.list;
export const tileIcon = I;

export function Svg({ d, size = 18, stroke = 1.7, style }: { d: string; size?: number; stroke?: number; style?: React.CSSProperties }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={stroke} strokeLinecap="round" strokeLinejoin="round" style={{ flex: 'none', ...style }} aria-hidden="true">
      <path d={d} />
    </svg>
  );
}

/** BlueMeal logo mark (same SVG as the design and BrandLogo). */
export function LogoMark({ size = 34 }: { size?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 64 64" aria-hidden="true" style={{ flex: 'none' }}>
      <circle cx="32" cy="32" r="30" fill="#3f8f5a" />
      <path d="M18 34c0-8 6-14 14-14s14 6 14 14H18z" fill="#fff" />
      <rect x="14" y="36" width="36" height="4" rx="2" fill="#fff" />
      <path d="M38 14c4 0 8 3 8 8-5 0-8-3-8-8z" fill="#bfe3c8" />
    </svg>
  );
}
