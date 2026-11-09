import type { ReactNode } from 'react';
import type { z } from 'zod';

export type FieldType = 'text' | 'textarea' | 'number' | 'money' | 'percent' | 'switch' | 'enum' | 'lookup' | 'date' | 'time' | 'email' | 'custom';

export interface FieldDef {
  name: string;
  /** i18n key (namespace-qualified allowed, e.g. "catalog:fields.refuseRate") */
  label: string;
  type: FieldType;
  required?: boolean;
  /** zod schema used for validation (bilingual messages via zodRule) */
  schema?: z.ZodType;
  /** enum name for type=enum (labels from enums:<Enum>.<Value>) */
  enumName?: string;
  enumValues?: string[];
  /** catalog resource for type=lookup, e.g. "food-groups" */
  lookup?: string;
  multiple?: boolean;
  min?: number;
  max?: number;
  step?: number;
  precision?: number;
  addonAfter?: string;
  /** grid span out of 24 (default 12, textarea 24) */
  span?: number;
  placeholder?: string;
  tooltip?: string;
  disabled?: boolean | ((values: Record<string, unknown>) => boolean);
  hidden?: (values: Record<string, unknown>) => boolean;
  render?: (props: { value: unknown; onChange: (v: unknown) => void; values: Record<string, unknown> }) => ReactNode;
}

export interface ColumnDef<T> {
  key: string;
  label: string;
  dataIndex?: string | string[];
  width?: number;
  fixed?: 'left' | 'right';
  sortKey?: string;
  align?: 'left' | 'right' | 'center';
  render?: (value: any, record: T) => ReactNode;
  /** hidden by default (user can enable via column settings) */
  defaultHidden?: boolean;
  /** shows on small screens */
  mobile?: boolean;
}

export interface FilterDef {
  name: string;
  label: string;
  type: 'enum' | 'lookup' | 'select' | 'boolean';
  enumName?: string;
  enumValues?: string[];
  lookup?: string;
  options?: { value: string; label: string }[];
}

export interface CatalogConfig<T extends { id: string }> {
  /** REST resource under /api/v1 */
  resource: string;
  /** permission module prefix, e.g. "department" → department.view/create/edit/delete/import/export */
  perm: string;
  /** page title (i18n key) */
  title: string;
  columns: ColumnDef<T>[];
  fields: FieldDef[];
  filters?: FilterDef[];
  defaults?: Record<string, unknown>;
  /** whether the entity has IsActive (lock/unlock) */
  hasActive?: boolean;
  hasCode?: boolean;
  hideImport?: boolean;
  hideExport?: boolean;
  formWidth?: number;
  /** maps a record to form initial values (default: the record itself) */
  toForm?: (r: T) => Record<string, unknown>;
  /** maps form values to the request body */
  toBody?: (v: Record<string, unknown>) => Record<string, unknown>;
  rowActions?: (record: T, reload: () => void) => ReactNode;
  toolbar?: (reload: () => void) => ReactNode;
  /** custom editor tabs rendered under the main form (e.g. contracts of a partner) */
  detailTabs?: (record: T) => { key: string; label: string; children: ReactNode }[];
  defaultSort?: string;
}
