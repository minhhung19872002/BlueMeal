import { Tag } from 'antd';
import { useEnumLabel } from '@/shared/components/catalog/inputs';
import type { CostView, NutritionView, WarningItem } from '@/shared/components/nutrition/NutritionPanel';

export interface MenuSnapshot { nutrition: NutritionView; cost?: CostView | null; warnings: WarningItem[]; computedAt: string; recommendationId?: string; targetEnergyKcal?: number }

export interface MenuItemRow { id?: string; dishId: string; dishCode?: string; dishNameVi?: string; dishNameEn?: string; dishType?: string; cookingMethod?: string; recipeId?: string | null; servingFactor: number; slot?: string; sortOrder?: number }

export interface MenuDetail {
  id: string; code: string; planId?: string; menuDate: string; mealShiftId: string; shift?: string; shiftEn?: string; subjectGroupId?: string; customerId?: string;
  recommendationId?: string; structureTemplateId?: string; servings: number; budgetPerServing?: number; creationMode: string; status: string; approvalLevel: number;
  nutritionScore?: number; energyKcal?: number; costPerServing?: number; criticalWarnings: number; criticalOverrideReason?: string; note?: string; ownerUserId?: string; version: number;
  explanation?: { vi: string; en: string; seed?: number; score?: number } | null;
  items: MenuItemRow[]; snapshot?: MenuSnapshot | null;
  approvals: { id: string; action: string; level: number; userName?: string; at: string; comment?: string; from: string; to: string }[];
}

export const MENU_STATUS_COLOR: Record<string, string> = {
  Draft: 'default', Submitted: 'processing', Approved: 'green', Rejected: 'red', Returned: 'orange', Published: 'cyan', Cancelled: 'default',
};

export function MenuStatusTag({ status }: { status: string }) {
  const enumLabel = useEnumLabel();
  return <Tag color={MENU_STATUS_COLOR[status]}>{enumLabel('MenuStatus', status)}</Tag>;
}

export const EDITABLE = ['Draft', 'Returned', 'Rejected'];
