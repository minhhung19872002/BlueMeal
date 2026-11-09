import type { AppRoute } from './router';

/**
 * All authenticated routes. Each page is lazy-loaded. Keep in sync with navigation.tsx.
 * Permissions here only drive the friendly 403 page — the API enforces every permission server-side.
 */
export const appRoutes: AppRoute[] = [
  { path: '', page: () => import('@/features/dashboard/DashboardPage') },
  { path: 'profile', page: () => import('@/features/auth/ProfilePage') },
  { path: 'admin/version', page: () => import('@/features/admin/VersionPage'), perms: ['system.version'] },
  { path: 'admin/organization', page: () => import('@/features/admin/OrganizationPage'), perms: ['organization.view'] },
  { path: 'admin/departments', page: () => import('@/features/admin/CommonCatalogPages').then((m) => ({ default: m.DepartmentsPage })), perms: ['department.view'] },
  { path: 'admin/employees', page: () => import('@/features/admin/EmployeesPage'), perms: ['employee.view'] },
  { path: 'admin/roles', page: () => import('@/features/admin/RolesPage'), perms: ['role.view'] },
  { path: 'admin/users', page: () => import('@/features/admin/UsersPage'), perms: ['user.view'] },
  { path: 'admin/sessions', page: () => import('@/features/admin/SecurityPages').then((m) => ({ default: m.SessionsPage })), perms: ['session.view'] },
  { path: 'admin/permissions', page: () => import('@/features/admin/SecurityPages').then((m) => ({ default: m.PermissionsPage })), perms: ['permission.view'] },
  { path: 'admin/audit-logs', page: () => import('@/features/admin/SecurityPages').then((m) => ({ default: m.AuditLogsPage })), perms: ['audit.view'] },
  { path: 'admin/settings', page: () => import('@/features/admin/SettingsPage'), perms: ['system.setting'] },
  { path: 'catalog/countries', page: () => import('@/features/admin/CommonCatalogPages').then((m) => ({ default: m.CountriesPage })), perms: ['common_catalog.view'] },
  { path: 'catalog/provinces', page: () => import('@/features/admin/CommonCatalogPages').then((m) => ({ default: m.ProvincesPage })), perms: ['common_catalog.view'] },
  { path: 'catalog/wards', page: () => import('@/features/admin/CommonCatalogPages').then((m) => ({ default: m.WardsPage })), perms: ['common_catalog.view'] },
  { path: 'catalog/units', page: () => import('@/features/admin/CommonCatalogPages').then((m) => ({ default: m.UnitsPage })), perms: ['common_catalog.view'] },
  { path: 'catalog/subjects', page: () => import('@/features/catalogs/PartnerCustomerPages').then((m) => ({ default: m.SubjectGroupsPage })), perms: ['subject.view'] },
  { path: 'catalog/partner-types', page: () => import('@/features/catalogs/PartnerCustomerPages').then((m) => ({ default: m.PartnerTypesPage })), perms: ['partner.view'] },
  { path: 'catalog/partners', page: () => import('@/features/catalogs/PartnerCustomerPages').then((m) => ({ default: m.PartnersPage })), perms: ['partner.view'] },
  { path: 'catalog/customer-types', page: () => import('@/features/catalogs/PartnerCustomerPages').then((m) => ({ default: m.CustomerTypesPage })), perms: ['customer.view'] },
  { path: 'catalog/customers', page: () => import('@/features/catalogs/PartnerCustomerPages').then((m) => ({ default: m.CustomersPage })), perms: ['customer.view'] },
  { path: 'catalog/warehouses', page: () => import('@/features/catalogs/FoodPages').then((m) => ({ default: m.WarehousesPage })), perms: ['warehouse.view'] },
  { path: 'catalog/food-groups', page: () => import('@/features/catalogs/FoodPages').then((m) => ({ default: m.FoodGroupsPage })), perms: ['food_group.view'] },
  { path: 'catalog/nutrients', page: () => import('@/features/catalogs/FoodPages').then((m) => ({ default: m.NutrientsPage })), perms: ['nutrient.view'] },
  { path: 'catalog/foods', page: () => import('@/features/catalogs/FoodPages').then((m) => ({ default: m.FoodsPage })), perms: ['food.view'] },
  { path: 'catalog/ingredients', page: () => import('@/features/catalogs/FoodPages').then((m) => ({ default: m.IngredientsPage })), perms: ['ingredient.view'] },
  { path: 'safety/specs', page: () => import('@/features/catalogs/FoodPages').then((m) => ({ default: m.FoodSafetySpecsPage })), perms: ['food_safety.view'] },
  { path: 'catalog/meal-shifts', page: () => import('@/features/catalogs/FoodPages').then((m) => ({ default: m.MealShiftsPage })), perms: ['meal_shift.view'] },
  { path: 'catalog/meal-structures', page: () => import('@/features/catalogs/FoodPages').then((m) => ({ default: m.MealStructuresPage })), perms: ['meal_structure.view'] },
  { path: 'catalog/dishes', page: () => import('@/features/catalogs/DishPages').then((m) => ({ default: m.DishesPage })), perms: ['dish.view'] },
  { path: 'catalog/recommendations', page: () => import('@/features/catalogs/DishPages').then((m) => ({ default: m.RecommendationsPage })), perms: ['recommendation.view'] },
  { path: 'catalog/report-groups', page: () => import('@/features/catalogs/DishPages').then((m) => ({ default: m.ReportGroupsPage })), perms: ['report_group.view'] },
  { path: 'recipes', page: () => import('@/features/recipes/RecipesPage'), perms: ['recipe.view'] },
  { path: 'recipes/:id', page: () => import('@/features/recipes/RecipeEditorPage'), perms: ['recipe.view'] },
];
