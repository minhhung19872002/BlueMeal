import { useState, type ReactNode } from 'react';
import { App, Button, Form, Modal, Popconfirm, Space, Table, Tooltip } from 'antd';
import type { TableColumnsType } from 'antd';
import { DeleteOutlined, EditOutlined, PlusOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';
import { CatalogFormFields } from './catalog/CatalogForm';
import type { FieldDef } from './catalog/types';

/**
 * Child collection editor used in catalog detail tabs (contracts, certificates, contacts, prices…).
 * GET {url} → array; POST {url}; PUT {url}/{id}; DELETE {url}/{id}.
 */
export function SubTable<T extends { id: string }>({
  url,
  columns,
  fields,
  canEdit,
  allowUpdate = true,
  allowDelete = true,
  toForm,
  toBody,
  extra,
  title,
}: {
  url: string;
  columns: TableColumnsType<T>;
  fields: FieldDef[];
  canEdit: boolean;
  allowUpdate?: boolean;
  allowDelete?: boolean;
  toForm?: (r: T) => Record<string, unknown>;
  toBody?: (v: Record<string, any>) => Record<string, unknown>;
  extra?: ReactNode;
  title?: string;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [editing, setEditing] = useState<T | 'new' | null>(null);
  const [form] = Form.useForm();
  const { data, isLoading, refetch } = useQuery({ queryKey: ['sub', url], queryFn: async () => (await http.get<T[]>(url)).data });

  const save = async () => {
    const v = await form.validateFields();
    const body = toBody ? toBody(v) : v;
    try {
      if (editing === 'new') await http.post(url, body);
      else if (editing) await http.put(`${url}/${editing.id}`, { ...body, version: (editing as any).version });
      setEditing(null);
      void refetch();
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  return (
    <Space direction="vertical" style={{ width: '100%' }}>
      <Space wrap>
        {canEdit && (
          <Button size="small" icon={<PlusOutlined />} onClick={() => { setEditing('new'); form.resetFields(); }}>
            {t('actions.add')}
          </Button>
        )}
        {extra}
      </Space>
      <Table<T>
        size="small"
        rowKey="id"
        loading={isLoading}
        dataSource={data}
        pagination={data && data.length > 10 ? { pageSize: 10 } : false}
        scroll={{ x: 'max-content' }}
        columns={[
          ...columns,
          ...(canEdit
            ? [{
                key: '_a', width: 90, fixed: 'right' as const,
                render: (_: unknown, r: T) => (
                  <Space size={0}>
                    {allowUpdate && (
                      <Tooltip title={t('actions.edit')}>
                        <Button type="text" size="small" icon={<EditOutlined />} onClick={() => { setEditing(r); form.resetFields(); form.setFieldsValue(toForm ? toForm(r) : r); }} />
                      </Tooltip>
                    )}
                    {allowDelete && (
                      <Popconfirm title={t('confirmDelete')} onConfirm={async () => { try { await http.delete(`${url}/${r.id}`); void refetch(); } catch (e) { message.error(problemMessage(e)); } }}>
                        <Button type="text" size="small" danger icon={<DeleteOutlined />} />
                      </Popconfirm>
                    )}
                  </Space>
                ),
              }]
            : []),
        ]}
      />
      <Modal open={!!editing} title={title} onCancel={() => setEditing(null)} onOk={() => void save()} width={720} destroyOnHidden okText={t('actions.save')}>
        <Form form={form} layout="vertical">
          <CatalogFormFields fields={fields} form={form} />
        </Form>
      </Modal>
    </Space>
  );
}
