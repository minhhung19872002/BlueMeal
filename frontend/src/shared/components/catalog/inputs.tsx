import { useEffect, useMemo, useState } from 'react';
import { Select, Spin } from 'antd';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { http } from '@/shared/api/http';

export interface LookupItem {
  id: string;
  code: string;
  nameVi: string;
  nameEn?: string | null;
}

export function lookupLabel(i: LookupItem, lang: string) {
  const name = lang === 'en' && i.nameEn ? i.nameEn : i.nameVi;
  return `${i.code} — ${name}`;
}

function useDebounced<T>(value: T, ms = 300) {
  const [v, setV] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setV(value), ms);
    return () => clearTimeout(id);
  }, [value, ms]);
  return v;
}

/** Server-side accent-insensitive combobox over /api/v1/{resource}/lookup (SPEC §7.3.5). */
export function LookupSelect({
  resource,
  value,
  onChange,
  multiple,
  placeholder,
  allowClear = true,
  disabled,
  style,
  extraParams,
  'data-testid': testId,
}: {
  resource: string;
  value?: string | string[] | null;
  onChange?: (v: any, item?: LookupItem | LookupItem[]) => void;
  multiple?: boolean;
  placeholder?: string;
  allowClear?: boolean;
  disabled?: boolean;
  style?: React.CSSProperties;
  extraParams?: Record<string, string>;
  'data-testid'?: string;
}) {
  const { i18n, t } = useTranslation();
  const [search, setSearch] = useState('');
  const q = useDebounced(search);
  const { data, isFetching } = useQuery({
    queryKey: ['lookup', resource, q, extraParams],
    queryFn: async () => (await http.get<LookupItem[]>(`/${resource}/lookup`, { params: { q, take: 50, ...extraParams } })).data,
    staleTime: 60_000,
  });
  // make sure the currently selected values are labeled even if not in the first page
  const selectedIds = useMemo(() => (Array.isArray(value) ? value : value ? [value] : []), [value]);
  const missing = selectedIds.filter((id) => !data?.some((d) => d.id === id));
  const { data: selected } = useQuery({
    queryKey: ['lookup-selected', resource, missing.join(',')],
    enabled: missing.length > 0,
    queryFn: async () => Promise.all(missing.map(async (id) => (await http.get<LookupItem>(`/${resource}/${id}`)).data)),
    staleTime: 300_000,
  });
  const items = [...(data ?? []), ...(selected ?? []).filter((s) => !data?.some((d) => d.id === s.id))];
  return (
    <Select
      showSearch
      mode={multiple ? 'multiple' : undefined}
      value={value ?? undefined}
      onChange={(v) => onChange?.(v ?? null, Array.isArray(v) ? items.filter((i) => v.includes(i.id)) : items.find((i) => i.id === v))}
      filterOption={false}
      onSearch={setSearch}
      allowClear={allowClear}
      disabled={disabled}
      placeholder={placeholder ?? t('actions.select')}
      notFoundContent={isFetching ? <Spin size="small" /> : undefined}
      options={items.map((i) => ({ value: i.id, label: lookupLabel(i, i18n.language) }))}
      style={{ width: '100%', ...style }}
      data-testid={testId}
      popupMatchSelectWidth={false}
    />
  );
}

/** Enum select with localized labels from enums:<EnumName>.<Value>. */
export function EnumSelect({
  enumName,
  values,
  value,
  onChange,
  allowClear,
  multiple,
  placeholder,
  disabled,
  style,
}: {
  enumName: string;
  values: string[];
  value?: string | string[] | null;
  onChange?: (v: any) => void;
  allowClear?: boolean;
  multiple?: boolean;
  placeholder?: string;
  disabled?: boolean;
  style?: React.CSSProperties;
}) {
  const { t } = useTranslation('enums');
  return (
    <Select
      value={value ?? undefined}
      onChange={(v) => onChange?.(v ?? null)}
      allowClear={allowClear}
      mode={multiple ? 'multiple' : undefined}
      disabled={disabled}
      placeholder={placeholder}
      options={values.map((v) => ({ value: v, label: t(`${enumName}.${v}`, v) }))}
      style={{ width: '100%', ...style }}
    />
  );
}

/** Localized enum label helper. */
export function useEnumLabel() {
  const { t } = useTranslation('enums');
  return (enumName: string, v: string | null | undefined) => (v ? t(`${enumName}.${v}`, v) : '');
}
