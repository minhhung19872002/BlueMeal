import type { ReactNode } from 'react';
import {
  BarChartOutlined, CalendarOutlined, ClusterOutlined, DashboardOutlined, ExperimentOutlined,
  FileSearchOutlined, InboxOutlined, SafetyCertificateOutlined, SettingOutlined, TeamOutlined,
} from '@ant-design/icons';

export interface NavItem {
  key: string;
  /** i18n key in the "common" namespace (nav.*) */
  label: string;
  path?: string;
  icon?: ReactNode;
  /** Visible when the user has ANY of these permissions (empty = any authenticated user). */
  perms?: string[];
  children?: NavItem[];
}

/**
 * Sidebar definition. Items without permission are hidden (SPEC §9). Server-side checks remain authoritative.
 * Routes are registered in router.tsx; keep paths in sync.
 */
export const NAV: NavItem[] = [
  { key: 'dashboard', label: 'nav.dashboard', path: '/', icon: <DashboardOutlined />, perms: ['dashboard.view'] },
  {
    key: 'menus', label: 'nav.menus', icon: <CalendarOutlined />,
    children: [
      { key: 'menu-plans', label: 'nav.menuPlans', path: '/menus/plans', perms: ['menu_plan.view'] },
      { key: 'menu-list', label: 'nav.menuList', path: '/menus', perms: ['menu.view'] },
      { key: 'menu-auto', label: 'nav.menuAuto', path: '/menus/auto', perms: ['menu.create'] },
      { key: 'menu-manual', label: 'nav.menuManual', path: '/menus/new', perms: ['menu.create'] },
      { key: 'menu-approval', label: 'nav.menuApproval', path: '/menus/approval', perms: ['menu.approve', 'menu.approve_final'] },
      { key: 'menu-published', label: 'nav.menuPublished', path: '/menus/published', perms: ['menu.view'] },
      { key: 'meal-orders', label: 'nav.mealOrders', path: '/meal-orders', perms: ['meal_order.view'] },
    ],
  },
  {
    key: 'analysis', label: 'nav.analysis', icon: <BarChartOutlined />,
    children: [
      { key: 'nutrition-analysis', label: 'nav.nutritionAnalysis', path: '/analysis/nutrition', perms: ['nutrition.view'] },
      { key: 'alerts', label: 'nav.alerts', path: '/alerts', perms: ['alert.view'] },
      { key: 'cost-sheet', label: 'nav.costSheet', path: '/cost-sheet', perms: ['cost.view'] },
    ],
  },
  {
    key: 'food', label: 'nav.food', icon: <ExperimentOutlined />,
    children: [
      { key: 'foods', label: 'nav.foods', path: '/catalog/foods', perms: ['food.view'] },
      { key: 'ingredients', label: 'nav.ingredients', path: '/catalog/ingredients', perms: ['ingredient.view'] },
      { key: 'dishes', label: 'nav.dishes', path: '/catalog/dishes', perms: ['dish.view'] },
      { key: 'recipes', label: 'nav.recipes', path: '/recipes', perms: ['recipe.view'] },
      { key: 'recommendations', label: 'nav.recommendations', path: '/catalog/recommendations', perms: ['recommendation.view'] },
      { key: 'food-groups', label: 'nav.foodGroups', path: '/catalog/food-groups', perms: ['food_group.view'] },
      { key: 'nutrients', label: 'nav.nutrients', path: '/catalog/nutrients', perms: ['nutrient.view'] },
      { key: 'meal-shifts', label: 'nav.mealShifts', path: '/catalog/meal-shifts', perms: ['meal_shift.view'] },
      { key: 'meal-structures', label: 'nav.mealStructures', path: '/catalog/meal-structures', perms: ['meal_structure.view'] },
      { key: 'warehouses', label: 'nav.warehouses', path: '/catalog/warehouses', perms: ['warehouse.view'] },
      { key: 'report-groups', label: 'nav.reportGroups', path: '/catalog/report-groups', perms: ['report_group.view'] },
    ],
  },
  {
    key: 'stock', label: 'nav.stock', icon: <InboxOutlined />,
    children: [
      { key: 'stock-receipts', label: 'nav.stockReceipts', path: '/stock/receipts', perms: ['stock.receipt.view'] },
      { key: 'stock-issues', label: 'nav.stockIssues', path: '/stock/issues', perms: ['stock.issue.view'] },
      { key: 'stock-takes', label: 'nav.stockTakes', path: '/stock/takes', perms: ['stock.take.view'] },
      { key: 'stock-balance', label: 'nav.stockBalance', path: '/stock/balance', perms: ['stock.view'] },
      { key: 'stock-card', label: 'nav.stockCard', path: '/stock/card', perms: ['stock.view'] },
      { key: 'purchase-requests', label: 'nav.purchaseRequests', path: '/stock/purchase-requests', perms: ['purchase.view'] },
      { key: 'period-locks', label: 'nav.periodLocks', path: '/stock/locks', perms: ['stock.lock'] },
    ],
  },
  {
    key: 'safety', label: 'nav.safety', icon: <SafetyCertificateOutlined />,
    children: [
      { key: 'inspections', label: 'nav.inspections', path: '/safety/inspections', perms: ['inspection.view'] },
      { key: 'samples', label: 'nav.samples', path: '/safety/samples', perms: ['sample.view'] },
      { key: 'food-safety', label: 'nav.foodSafety', path: '/safety/specs', perms: ['food_safety.view'] },
      { key: 'safety-dashboard', label: 'nav.safetyDashboard', path: '/safety/dashboard', perms: ['food_safety.view', 'stock.view'] },
    ],
  },
  {
    key: 'partners', label: 'nav.partnersCustomers', icon: <TeamOutlined />,
    children: [
      { key: 'subjects', label: 'nav.subjects', path: '/catalog/subjects', perms: ['subject.view'] },
      { key: 'partners-list', label: 'nav.partners', path: '/catalog/partners', perms: ['partner.view'] },
      { key: 'partner-types', label: 'nav.partnerTypes', path: '/catalog/partner-types', perms: ['partner.view'] },
      { key: 'customers', label: 'nav.customers', path: '/catalog/customers', perms: ['customer.view'] },
      { key: 'customer-types', label: 'nav.customerTypes', path: '/catalog/customer-types', perms: ['customer.view'] },
    ],
  },
  { key: 'reports', label: 'nav.reports', path: '/reports', icon: <FileSearchOutlined />, perms: ['report.view'] },
  {
    key: 'system', label: 'nav.system', icon: <SettingOutlined />,
    children: [
      { key: 'organization', label: 'nav.organization', path: '/admin/organization', perms: ['organization.view'] },
      { key: 'departments', label: 'nav.departments', path: '/admin/departments', perms: ['department.view'] },
      { key: 'employees', label: 'nav.employees', path: '/admin/employees', perms: ['employee.view'] },
      { key: 'roles', label: 'nav.roles', path: '/admin/roles', perms: ['role.view'] },
      { key: 'users', label: 'nav.users', path: '/admin/users', perms: ['user.view'] },
      { key: 'sessions', label: 'nav.sessions', path: '/admin/sessions', perms: ['session.view'] },
      { key: 'permissions', label: 'nav.permissions', path: '/admin/permissions', perms: ['permission.view'] },
      {
        key: 'common-catalogs', label: 'nav.commonCatalogs', icon: <ClusterOutlined />,
        children: [
          { key: 'countries', label: 'nav.countries', path: '/catalog/countries', perms: ['common_catalog.view'] },
          { key: 'provinces', label: 'nav.provinces', path: '/catalog/provinces', perms: ['common_catalog.view'] },
          { key: 'wards', label: 'nav.wards', path: '/catalog/wards', perms: ['common_catalog.view'] },
          { key: 'units', label: 'nav.units', path: '/catalog/units', perms: ['common_catalog.view'] },
        ],
      },
      { key: 'settings', label: 'nav.settings', path: '/admin/settings', perms: ['system.setting'] },
      { key: 'integration', label: 'nav.integration', path: '/admin/integration', perms: ['integration.view'] },
      { key: 'backups', label: 'nav.backups', path: '/admin/backups', perms: ['system.backup'] },
      { key: 'audit-logs', label: 'nav.auditLogs', path: '/admin/audit-logs', perms: ['audit.view'] },
      { key: 'version', label: 'nav.version', path: '/admin/version', perms: ['system.version'] },
    ],
  },
];

/** Filters the tree by permission, dropping empty groups. */
export function filterNav(items: NavItem[], has: (p: string) => boolean): NavItem[] {
  return items
    .map((i) => ({ ...i, children: i.children ? filterNav(i.children, has) : undefined }))
    .filter((i) => (i.children ? i.children.length > 0 : !i.perms || i.perms.length === 0 || i.perms.some(has)));
}

/** Finds the chain of nav items leading to a path (for breadcrumb and selected keys). */
export function findTrail(items: NavItem[], path: string, trail: NavItem[] = []): NavItem[] | null {
  let best: NavItem[] | null = null;
  for (const i of items) {
    const next = [...trail, i];
    if (i.path && (path === i.path || (i.path !== '/' && path.startsWith(i.path + '/')))) {
      if (!best || (best[best.length - 1].path?.length ?? 0) < i.path.length) best = next;
    }
    if (i.children) {
      const r = findTrail(i.children, path, next);
      if (r && (!best || (r[r.length - 1].path?.length ?? 0) > (best[best.length - 1].path?.length ?? 0))) best = r;
    }
  }
  return best;
}
