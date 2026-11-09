import { useMemo, useState } from 'react';
import { App, Button, Card, Checkbox, Drawer, Dropdown, Form, Grid, Input, Modal, Popover, Select, Space, Table, Tabs, Tag, Tooltip, Typography } from 'antd';
import type { TableProps } from 'antd';
import { DeleteOutlined, DownloadOutlined, EditOutlined, HistoryOutlined, LockOutlined, MoreOutlined, PlusOutlined, ReloadOutlined, SettingOutlined, UnlockOutlined, UploadOutlined } from '@ant-design/icons';
import { keepPreviousData, useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { downloadFile, http, problemFieldErrors, problemMessage, type PagedResult } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { useTablePrefs } from '@/shared/hooks/useTablePrefs';
import { CatalogFormFields } from './CatalogForm';
import { HistoryDrawer } from './HistoryDrawer';
import { ImportModal } from './ImportModal';
import { EnumSelect, LookupSelect } from './inputs';
import type { CatalogConfig } from './types';

interface Row {
  id: string;
  isActive?: boolean;
  version?: number;
  [k: string]: unknown;
}

/**
 * Generic catalog screen (SPEC §7 common features): server-side paging, accent-insensitive search, filters,
 * sorting, fixed/hidden columns saved per user, add/edit/soft-delete, lock/unlock, change history,
 * Excel import (template → preview → commit) and export. Responsive down to 360px.
 */
export function CatalogPage<T extends Row>({ config }: { config: CatalogConfig<T> }) {
  const { t } = useTranslation(['common', 'catalog']);
  const { message, modal } = App.useApp();
  const { has } = usePermission();
  const screens = Grid.useBreakpoint();
  const isMobile = !screens.md;
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [sort, setSort] = useState<string | undefined>(config.defaultSort);
  const [q, setQ] = useState('');
  const [search, setSearch] = useState('');
  const [active, setActive] = useState<string | undefined>(undefined);
  const [filters, setFilters] = useState<Record<string, string | undefined>>({});
  const [editing, setEditing] = useState<T | 'new' | null>(null);
  const [historyId, setHistoryId] = useState<string | undefined>();
  const [importOpen, setImportOpen] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form] = Form.useForm();
  const hasActive = config.hasActive ?? true;

  const can = (a: 'view' | 'create' | 'edit' | 'delete' | 'import' | 'export') => has(`${config.perm}.${a}`);
  const { hidden, setHidden } = useTablePrefs(config.resource, config.columns.filter((c) => c.defaultHidden).map((c) => c.key));

  const params = { page, pageSize, sort, q: search || undefined, isActive: active, ...filters };
  const { data, isFetching, refetch } = useQuery({
    queryKey: ['catalog', config.resource, params],
    queryFn: async () => (await http.get<PagedResult<T>>(`/${config.resource}`, { params })).data,
    placeholderData: keepPreviousData,
  });
  const reload = () => void refetch();

  const openEdit = (r: T | 'new') => {
    setEditing(r);
    form.resetFields();
    form.setFieldsValue(r === 'new' ? { isActive: true, sortOrder: 0, ...config.defaults } : config.toForm ? config.toForm(r) : r);
  };

  const closeEdit = () => {
    if (form.isFieldsTouched()) {
      modal.confirm({ title: t('confirmLeave'), onOk: () => setEditing(null) });
    } else setEditing(null);
  };

  const save = async () => {
    const values = await form.validateFields();
    const body = config.toBody ? config.toBody(values) : values;
    setSaving(true);
    try {
      if (editing === 'new') await http.post(`/${config.resource}`, body);
      else if (editing) await http.put(`/${config.resource}/${editing.id}`, { ...body, version: editing.version });
      message.success(t('saved'));
      setEditing(null);
      reload();
    } catch (e) {
      const fe = problemFieldErrors(e);
      const fields = Object.entries(fe).map(([name, err]) => ({ name, errors: [err] }));
      if (fields.length) form.setFields(fields as never);
      message.error(problemMessage(e));
    } finally {
      setSaving(false);
    }
  };

  const action = async (fn: () => Promise<unknown>) => {
    try {
      await fn();
      message.success(t('saved'));
      reload();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const visibleCols = config.columns.filter((c) => !hidden.includes(c.key) && (!isMobile || c.mobile !== false));
  const columns: TableProps<T>['columns'] = useMemo(
    () => [
      ...visibleCols.map((c) => ({
        key: c.key,
        title: t(c.label),
        dataIndex: (c.dataIndex ?? c.key) as never,
        width: c.width,
        fixed: isMobile ? undefined : c.fixed,
        align: c.align,
        sorter: c.sortKey ? true : undefined,
        sortOrder: sort === c.sortKey ? ('ascend' as const) : sort === '-' + c.sortKey ? ('descend' as const) : null,
        render: c.render as never,
        ellipsis: !c.render,
      })),
      ...(hasActive
        ? [{ key: 'isActive', title: t('fields.status'), dataIndex: 'isActive', width: 110, render: (v: boolean) => (v ? <Tag color="green">{t('status.active')}</Tag> : <Tag>{t('status.inactive')}</Tag>) }]
        : []),
      {
        key: '_actions',
        title: t('fields.actions'),
        fixed: isMobile ? undefined : ('right' as const),
        width: 120,
        render: (_: unknown, r: T) => (
          <Space size={0}>
            {can('edit') && (
              <Tooltip title={t('actions.edit')}>
                <Button type="text" icon={<EditOutlined />} onClick={() => openEdit(r)} data-testid="row-edit" />
              </Tooltip>
            )}
            <Dropdown
              trigger={['click']}
              menu={{
                items: [
                  ...(hasActive && can('edit')
                    ? [r.isActive
                        ? { key: 'lock', icon: <LockOutlined />, label: t('actions.lock'), onClick: () => void action(() => http.post(`/${config.resource}/${r.id}/lock`)) }
                        : { key: 'unlock', icon: <UnlockOutlined />, label: t('actions.unlock'), onClick: () => void action(() => http.post(`/${config.resource}/${r.id}/unlock`)) }]
                    : []),
                  { key: 'history', icon: <HistoryOutlined />, label: t('actions.history'), onClick: () => setHistoryId(r.id) },
                  ...(can('delete')
                    ? [{
                        key: 'delete', icon: <DeleteOutlined />, danger: true, label: t('actions.delete'),
                        onClick: () => modal.confirm({ title: t('confirmDelete'), okButtonProps: { danger: true }, onOk: () => action(() => http.delete(`/${config.resource}/${r.id}`)) }),
                      }]
                    : []),
                ],
              }}
            >
              <Button type="text" icon={<MoreOutlined />} aria-label={t('actions.more')} />
            </Dropdown>
            {config.rowActions?.(r, reload)}
          </Space>
        ),
      },
    ],
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [visibleCols.map((c) => c.key).join(','), sort, isMobile, t],
  );

  const onTableChange: TableProps<T>['onChange'] = (p, _f, s) => {
    setPage(p.current ?? 1);
    setPageSize(p.pageSize ?? 20);
    const so = Array.isArray(s) ? s[0] : s;
    const col = config.columns.find((c) => c.key === so?.columnKey);
    setSort(so?.order && col?.sortKey ? (so.order === 'descend' ? '-' : '') + col.sortKey : config.defaultSort);
  };

  const columnSettings = (
    <Space direction="vertical">
      {config.columns.map((c) => (
        <Checkbox
          key={c.key}
          checked={!hidden.includes(c.key)}
          onChange={(e) => setHidden(e.target.checked ? hidden.filter((h) => h !== c.key) : [...hidden, c.key])}
        >
          {t(c.label)}
        </Checkbox>
      ))}
    </Space>
  );

  const formBody = (
    <Form form={form} layout="vertical" requiredMark onFinish={() => void save()} data-testid="catalog-form">
      <CatalogFormFields fields={config.fields} form={form} />
    </Form>
  );
  const editingRecord = editing && editing !== 'new' ? editing : null;
  const formContent = editingRecord && config.detailTabs ? (
    <Tabs items={[{ key: 'main', label: t('catalog:tabs.general'), children: formBody }, ...config.detailTabs(editingRecord)]} />
  ) : (
    formBody
  );
  const formFooter = (
    <Space>
      <Button onClick={closeEdit}>{t('actions.cancel')}</Button>
      <Button type="primary" loading={saving} onClick={() => void save()} data-testid="catalog-save">
        {t('actions.save')}
      </Button>
    </Space>
  );
  const formTitle = editing === 'new' ? t('actions.add') + ' — ' + t(config.title) : t('actions.edit') + ' — ' + t(config.title);

  return (
    <Card>
      <div className="bm-page-header">
        <Typography.Title level={4}>{t(config.title)}</Typography.Title>
        <Space wrap>
          {config.toolbar?.(reload)}
          {can('import') && !config.hideImport && (
            <Button icon={<UploadOutlined />} onClick={() => setImportOpen(true)}>
              {t('actions.import')}
            </Button>
          )}
          {can('export') && !config.hideExport && (
            <Button icon={<DownloadOutlined />} onClick={() => void downloadFile(`/${config.resource}/export`, { ...params, page: undefined, pageSize: undefined }).catch((e) => message.error(problemMessage(e)))}>
              {t('actions.export')}
            </Button>
          )}
          {can('create') && (
            <Button type="primary" icon={<PlusOutlined />} onClick={() => openEdit('new')} data-testid="catalog-add">
              {t('actions.add')}
            </Button>
          )}
        </Space>
      </div>
      <div className="bm-toolbar">
        <Input.Search
          allowClear
          placeholder={t('searchPlaceholder')}
          value={q}
          onChange={(e) => setQ(e.target.value)}
          onSearch={(v) => {
            setSearch(v.trim());
            setPage(1);
          }}
          style={{ width: isMobile ? '100%' : 320 }}
          data-testid="catalog-search"
        />
        {config.filters?.map((f) => (
          <div key={f.name} style={{ width: isMobile ? '100%' : 220 }}>
            {f.type === 'lookup' ? (
              <LookupSelect resource={f.lookup!} placeholder={t(f.label)} value={filters[f.name]} onChange={(v) => { setFilters({ ...filters, [f.name]: v ?? undefined }); setPage(1); }} />
            ) : f.type === 'enum' ? (
              <EnumSelect enumName={f.enumName!} values={f.enumValues ?? []} allowClear placeholder={t(f.label)} value={filters[f.name]} onChange={(v) => { setFilters({ ...filters, [f.name]: v ?? undefined }); setPage(1); }} />
            ) : (
              <Select allowClear placeholder={t(f.label)} style={{ width: '100%' }} value={filters[f.name]} options={f.options?.map((o) => ({ ...o, label: t(o.label) }))} onChange={(v) => { setFilters({ ...filters, [f.name]: v ?? undefined }); setPage(1); }} />
            )}
          </div>
        ))}
        {hasActive && (
          <Select
            allowClear
            placeholder={t('fields.status')}
            style={{ width: isMobile ? '100%' : 160 }}
            value={active}
            onChange={(v) => { setActive(v); setPage(1); }}
            options={[{ value: 'true', label: t('status.active') }, { value: 'false', label: t('status.inactive') }]}
          />
        )}
        <Tooltip title={t('actions.refresh')}>
          <Button icon={<ReloadOutlined />} onClick={reload} aria-label={t('actions.refresh')} />
        </Tooltip>
        <Popover content={columnSettings} trigger="click" title={t('actions.columns')}>
          <Button icon={<SettingOutlined />} aria-label={t('actions.columns')} />
        </Popover>
      </div>
      <Table<T>
        rowKey="id"
        size="middle"
        loading={isFetching}
        columns={columns}
        dataSource={data?.items}
        onChange={onTableChange}
        scroll={{ x: 'max-content' }}
        sticky
        pagination={{
          current: page,
          pageSize,
          total: data?.totalItems,
          showSizeChanger: true,
          pageSizeOptions: [10, 20, 50, 100, 200],
          showTotal: (n) => t('totalItems', { count: n }),
          size: isMobile ? 'small' : 'default',
        }}
        onRow={(r) => ({ onDoubleClick: () => can('edit') && openEdit(r) })}
      />
      {isMobile ? (
        <Drawer open={!!editing} onClose={closeEdit} title={formTitle} width="100%" footer={formFooter} destroyOnHidden>
          {formContent}
        </Drawer>
      ) : (
        <Modal open={!!editing} onCancel={closeEdit} title={formTitle} width={config.formWidth ?? 760} footer={formFooter} destroyOnHidden maskClosable={false}>
          {formContent}
        </Modal>
      )}
      <HistoryDrawer resource={config.resource} id={historyId} open={!!historyId} onClose={() => setHistoryId(undefined)} />
      <ImportModal resource={config.resource} open={importOpen} onClose={() => setImportOpen(false)} onDone={reload} title={t(config.title)} />
    </Card>
  );
}

/** Quick helper to declare the standard code / names / sort columns and fields. */
export const std = {
  columns: <T,>(extra: CatalogConfig<T & Row>['columns'] = []) => [
    { key: 'code', label: 'fields.code', width: 130, fixed: 'left' as const, sortKey: 'code' },
    { key: 'nameVi', label: 'fields.nameVi', width: 260, sortKey: 'nameVi', mobile: true },
    { key: 'nameEn', label: 'fields.nameEn', width: 220, defaultHidden: true, mobile: false },
    ...extra,
    { key: 'sortOrder', label: 'fields.sortOrder', width: 90, align: 'right' as const, sortKey: 'sortOrder', defaultHidden: true, mobile: false },
  ],
  fields: (extra: CatalogConfig<Row>['fields'] = []) => [
    { name: 'code', label: 'fields.code', type: 'text' as const, required: true, span: 8 },
    { name: 'nameVi', label: 'fields.nameVi', type: 'text' as const, required: true, span: 16 },
    { name: 'nameEn', label: 'fields.nameEn', type: 'text' as const, span: 24 },
    ...extra,
    { name: 'sortOrder', label: 'fields.sortOrder', type: 'number' as const, span: 6, min: 0 },
    { name: 'isActive', label: 'fields.isActive', type: 'switch' as const, span: 6 },
  ],
};
