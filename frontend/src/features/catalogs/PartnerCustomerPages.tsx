import { Rate, Table, Tag, Tooltip } from 'antd';
import { WarningOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { z } from 'zod';
import { http } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { CatalogPage, std } from '@/shared/components/catalog/CatalogPage';
import { useEnumLabel } from '@/shared/components/catalog/inputs';
import { FileField } from '@/shared/components/FileField';
import { SubTable } from '@/shared/components/SubTable';
import { ENUMS } from '@/shared/utils/enums';
import { fmt } from '@/shared/utils/format';
import { zs } from '@/shared/utils/validation';

type Any = { id: string; [k: string]: any };

// ------------------------------------------------------------------ subject groups

export function SubjectGroupsPage() {
  const enumLabel = useEnumLabel();
  return (
    <CatalogPage<Any>
      config={{
        resource: 'subject-groups', perm: 'subject', title: 'nav.subjects',
        columns: std.columns([
          { key: 'laborIntensity', label: 'catalog:f.laborIntensity', width: 160, render: (v) => enumLabel('LaborIntensity', v) },
          { key: 'gender', label: 'catalog:f.gender', width: 90, render: (v) => enumLabel('Gender', v) },
          { key: 'age', label: 'catalog:f.ageFrom', width: 100, render: (_, r) => (r.ageFrom || r.ageTo ? `${r.ageFrom ?? ''}–${r.ageTo ?? ''}` : '') },
          { key: 'specialConditions', label: 'catalog:f.specialConditions', width: 200 },
        ]),
        fields: std.fields([
          { name: 'laborIntensity', label: 'catalog:f.laborIntensity', type: 'enum', enumName: 'LaborIntensity', enumValues: [...ENUMS.LaborIntensity], required: true, span: 8 },
          { name: 'gender', label: 'catalog:f.gender', type: 'enum', enumName: 'Gender', enumValues: [...ENUMS.Gender], span: 8 },
          { name: 'ageFrom', label: 'catalog:f.ageFrom', type: 'number', min: 14, max: 100, span: 4 },
          { name: 'ageTo', label: 'catalog:f.ageTo', type: 'number', min: 14, max: 100, span: 4 },
          { name: 'defaultRecommendationId', label: 'catalog:f.reference', type: 'lookup', lookup: 'recommendations', span: 24 },
          { name: 'specialConditions', label: 'catalog:f.specialConditions', type: 'text', span: 24 },
          { name: 'allergens', label: 'catalog:f.allergens', type: 'text', span: 24 },
          { name: 'note', label: 'catalog:f.note', type: 'textarea' },
        ]),
        defaults: { laborIntensity: 'Moderate', gender: 'Common' },
        filters: [{ name: 'laborIntensity', label: 'catalog:f.laborIntensity', type: 'enum', enumName: 'LaborIntensity', enumValues: [...ENUMS.LaborIntensity] }],
      }}
    />
  );
}

// ------------------------------------------------------------------ partner types / customer types

const simpleTypeConfig = (resource: string, perm: string, title: string) => ({
  resource, perm, title,
  columns: std.columns([{ key: 'description', label: 'catalog:f.description', width: 300 }]),
  fields: std.fields([{ name: 'description', label: 'catalog:f.description', type: 'textarea' as const }]),
});

export const PartnerTypesPage = () => <CatalogPage<Any> config={simpleTypeConfig('partner-types', 'partner', 'nav.partnerTypes')} />;
export const CustomerTypesPage = () => <CatalogPage<Any> config={simpleTypeConfig('customer-types', 'customer', 'nav.customerTypes')} />;

// ------------------------------------------------------------------ partners (suppliers) with tabs

function ExpiryTag({ days }: { days?: number | null }) {
  const { t } = useTranslation('catalog');
  if (days === null || days === undefined) return null;
  if (days < 0) return <Tag color="red">{t('sub.expired')}</Tag>;
  if (days <= 30) return <Tag color="orange">{t('sub.expiring')} ({days})</Tag>;
  return <Tag color="green">{days}</Tag>;
}

function PartnerReceipts({ partnerId }: { partnerId: string }) {
  const { t } = useTranslation('catalog');
  const { data } = useQuery({ queryKey: ['partner-receipts', partnerId], queryFn: async () => (await http.get<Any[]>(`/partners/${partnerId}/receipts`)).data });
  return (
    <Table size="small" rowKey="id" dataSource={data} scroll={{ x: 600 }} pagination={{ pageSize: 10 }}
      columns={[
        { title: t('sub.docNo'), dataIndex: 'docNo' }, { title: t('sub.docDate'), dataIndex: 'docDate', render: (v) => fmt.date(v) },
        { title: t('sub.warehouse'), dataIndex: 'warehouse' }, { title: t('sub.invoiceNo'), dataIndex: 'invoiceNo' },
        { title: t('sub.amount'), dataIndex: 'totalAmount', align: 'right', render: (v) => fmt.money(v) }, { title: t('sub.lines'), dataIndex: 'lines', align: 'right' },
      ]} />
  );
}

export function PartnersPage() {
  const { t } = useTranslation('catalog');
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const canEdit = has('partner.edit');
  return (
    <CatalogPage<Any>
      config={{
        resource: 'partners', perm: 'partner', title: 'nav.partners', formWidth: 900,
        columns: std.columns([
          { key: 'partnerTypeNameVi', label: 'catalog:f.partnerType', width: 160 },
          { key: 'taxCode', label: 'catalog:f.taxCode', width: 130 },
          { key: 'contactPerson', label: 'catalog:f.contactPerson', width: 160 },
          { key: 'phone', label: 'catalog:f.phone', width: 120 },
          { key: 'suppliedGroups', label: 'catalog:f.suppliedGroups', width: 180, defaultHidden: true },
          { key: 'rating', label: 'catalog:f.rating', width: 150, render: (v) => (v ? <Rate disabled allowHalf value={Number(v)} style={{ fontSize: 14 }} /> : null) },
          {
            key: 'nextCertificateExpiry', label: 'catalog:sub.expiryDate', width: 150,
            render: (v) => {
              if (!v) return null;
              const days = Math.round((new Date(v).getTime() - Date.now()) / 86_400_000);
              return <Tooltip title={fmt.date(v)}><span>{days <= 30 && <WarningOutlined style={{ color: days < 0 ? '#c8463d' : '#d99a1e' }} />} {fmt.date(v)}</span></Tooltip>;
            },
          },
        ]),
        fields: std.fields([
          { name: 'partnerTypeId', label: 'catalog:f.partnerType', type: 'lookup', lookup: 'partner-types' },
          { name: 'taxCode', label: 'catalog:f.taxCode', type: 'text', schema: z.string().regex(/^\d{10}(-\d{3})?$/, 'common:validation.number').optional().nullable() },
          { name: 'contactPerson', label: 'catalog:f.contactPerson', type: 'text', span: 8 },
          { name: 'phone', label: 'catalog:f.phone', type: 'text', span: 8 },
          { name: 'email', label: 'catalog:f.email', type: 'email', span: 8, schema: zs.email() },
          { name: 'address', label: 'catalog:f.address', type: 'text', span: 24 },
          { name: 'provinceId', label: 'catalog:f.province', type: 'lookup', lookup: 'provinces' },
          { name: 'wardId', label: 'catalog:f.ward', type: 'lookup', lookup: 'wards' },
          { name: 'bankAccount', label: 'catalog:f.bankAccount', type: 'text' },
          { name: 'bankName', label: 'catalog:f.bankName', type: 'text' },
          { name: 'suppliedGroups', label: 'catalog:f.suppliedGroups', type: 'text', span: 24 },
          { name: 'note', label: 'catalog:f.note', type: 'textarea' },
        ]),
        filters: [{ name: 'partnerTypeId', label: 'catalog:f.partnerType', type: 'lookup', lookup: 'partner-types' }],
        detailTabs: (r) => [
          {
            key: 'contracts', label: t('tabs.contracts'),
            children: (
              <SubTable<Any>
                url={`/partners/${r.id}/contracts`} canEdit={canEdit} title={t('tabs.contracts')}
                columns={[
                  { title: t('sub.contractNo'), dataIndex: 'contractNo' }, { title: t('sub.signedDate'), dataIndex: 'signedDate', render: (v) => fmt.date(v) },
                  { title: t('sub.validFrom'), dataIndex: 'validFrom', render: (v) => fmt.date(v) }, { title: t('sub.validTo'), dataIndex: 'validTo', render: (v) => fmt.date(v) },
                  { title: t('sub.value'), dataIndex: 'value', align: 'right', render: (v) => fmt.money(v) },
                  { title: t('sub.status'), dataIndex: 'status', render: (v) => enumLabel('ContractStatus', v) },
                  { title: t('sub.contractPrices'), dataIndex: 'prices', render: (p: Any[]) => p?.map((x) => <div key={x.ingredientId}>{x.ingredientName}: {fmt.money(x.unitPrice)}</div>) },
                ]}
                fields={[
                  { name: 'contractNo', label: 'catalog:sub.contractNo', type: 'text', required: true },
                  { name: 'status', label: 'catalog:sub.status', type: 'enum', enumName: 'ContractStatus', enumValues: [...ENUMS.ContractStatus], required: true },
                  { name: 'signedDate', label: 'catalog:sub.signedDate', type: 'date', required: true, span: 8 },
                  { name: 'validFrom', label: 'catalog:sub.validFrom', type: 'date', required: true, span: 8 },
                  { name: 'validTo', label: 'catalog:sub.validTo', type: 'date', span: 8 },
                  { name: 'value', label: 'catalog:sub.value', type: 'money', min: 0 },
                  { name: 'fileId', label: 'catalog:sub.file', type: 'custom', render: ({ value, onChange }) => <FileField value={value as string} onChange={onChange} ownerType="PartnerContract" /> },
                  { name: 'note', label: 'catalog:f.note', type: 'textarea' },
                ]}
              />
            ),
          },
          {
            key: 'certs', label: t('tabs.certificates'),
            children: (
              <SubTable<Any>
                url={`/partners/${r.id}/certificates`} canEdit={canEdit} title={t('tabs.certificates')}
                columns={[
                  { title: t('sub.certType'), dataIndex: 'certificateType', render: (v) => enumLabel('CertificateType', v) },
                  { title: t('sub.number'), dataIndex: 'number' }, { title: t('sub.issuer'), dataIndex: 'issuer' },
                  { title: t('sub.issuedDate'), dataIndex: 'issuedDate', render: (v) => fmt.date(v) }, { title: t('sub.expiryDate'), dataIndex: 'expiryDate', render: (v) => fmt.date(v) },
                  { title: t('sub.daysToExpiry'), dataIndex: 'daysToExpiry', render: (v) => <ExpiryTag days={v} /> },
                ]}
                fields={[
                  { name: 'certificateType', label: 'catalog:sub.certType', type: 'enum', enumName: 'CertificateType', enumValues: [...ENUMS.CertificateType], required: true },
                  { name: 'number', label: 'catalog:sub.number', type: 'text', required: true },
                  { name: 'issuer', label: 'catalog:sub.issuer', type: 'text', span: 24 },
                  { name: 'issuedDate', label: 'catalog:sub.issuedDate', type: 'date' },
                  { name: 'expiryDate', label: 'catalog:sub.expiryDate', type: 'date' },
                  { name: 'fileId', label: 'catalog:sub.file', type: 'custom', span: 24, render: ({ value, onChange }) => <FileField value={value as string} onChange={onChange} ownerType="PartnerCertificate" /> },
                  { name: 'note', label: 'catalog:f.note', type: 'textarea' },
                ]}
              />
            ),
          },
          { key: 'receipts', label: t('tabs.receipts'), children: <PartnerReceipts partnerId={r.id} /> },
          {
            key: 'ratings', label: t('tabs.ratings'),
            children: (
              <SubTable<Any>
                url={`/partners/${r.id}/ratings`} canEdit={canEdit} allowUpdate={false} allowDelete={false} title={t('tabs.ratings')}
                columns={[
                  { title: t('sub.ratedDate'), dataIndex: 'ratedDate', render: (v) => fmt.date(v) },
                  { title: t('sub.quality'), dataIndex: 'quality', render: (v) => <Rate disabled value={v} style={{ fontSize: 12 }} /> },
                  { title: t('sub.delivery'), dataIndex: 'delivery', render: (v) => <Rate disabled value={v} style={{ fontSize: 12 }} /> },
                  { title: t('sub.price'), dataIndex: 'price', render: (v) => <Rate disabled value={v} style={{ fontSize: 12 }} /> },
                  { title: t('sub.average'), dataIndex: 'average', render: (v) => fmt.number(v, 1) }, { title: t('sub.comment'), dataIndex: 'comment' },
                ]}
                fields={[
                  { name: 'ratedDate', label: 'catalog:sub.ratedDate', type: 'date', required: true, span: 24 },
                  { name: 'quality', label: 'catalog:sub.quality', type: 'custom', required: true, span: 8, render: ({ value, onChange }) => <Rate value={value as number} onChange={onChange} /> },
                  { name: 'delivery', label: 'catalog:sub.delivery', type: 'custom', required: true, span: 8, render: ({ value, onChange }) => <Rate value={value as number} onChange={onChange} /> },
                  { name: 'price', label: 'catalog:sub.price', type: 'custom', required: true, span: 8, render: ({ value, onChange }) => <Rate value={value as number} onChange={onChange} /> },
                  { name: 'comment', label: 'catalog:sub.comment', type: 'textarea' },
                ]}
              />
            ),
          },
        ],
      }}
    />
  );
}

// ------------------------------------------------------------------ customers with tabs

function CustomerOrders({ id }: { id: string }) {
  const { t } = useTranslation('catalog');
  const enumLabel = useEnumLabel();
  const { data } = useQuery({ queryKey: ['customer-orders', id], queryFn: async () => (await http.get<Any[]>(`/customers/${id}/orders`)).data });
  return (
    <Table size="small" rowKey="id" dataSource={data} pagination={{ pageSize: 10 }} scroll={{ x: 500 }}
      columns={[
        { title: t('sub.orderDate'), dataIndex: 'orderDate', render: (v) => fmt.date(v) },
        { title: t('sub.registered'), dataIndex: 'registeredServings', align: 'right' }, { title: t('sub.actual'), dataIndex: 'actualServings', align: 'right' },
        { title: t('sub.status'), dataIndex: 'status', render: (v) => enumLabel('MealOrderStatus', v) },
      ]} />
  );
}

function CustomerMenus({ id }: { id: string }) {
  const { t } = useTranslation('catalog');
  const { data } = useQuery({ queryKey: ['customer-menus', id], queryFn: async () => (await http.get<Any[]>(`/customers/${id}/published-menus`)).data });
  return (
    <Table size="small" rowKey="id" dataSource={data} pagination={{ pageSize: 10 }} scroll={{ x: 500 }}
      columns={[
        { title: t('sub.code'), dataIndex: 'code' }, { title: t('sub.menuDate'), dataIndex: 'menuDate', render: (v) => fmt.date(v) },
        { title: t('sub.shift'), dataIndex: 'shift' }, { title: t('sub.servings'), dataIndex: 'servings', align: 'right' },
        { title: t('sub.energy'), dataIndex: 'energyKcal', align: 'right', render: (v) => fmt.number(v, 0) },
      ]} />
  );
}

export function CustomersPage() {
  const { t } = useTranslation('catalog');
  const { has } = usePermission();
  const canEdit = has('customer.edit');
  return (
    <CatalogPage<Any>
      config={{
        resource: 'customers', perm: 'customer', title: 'nav.customers', formWidth: 900,
        columns: std.columns([
          { key: 'customerTypeNameVi', label: 'catalog:f.customerType', width: 170 },
          { key: 'workerCount', label: 'catalog:f.workerCount', width: 110, align: 'right', render: (v) => fmt.number(v) },
          { key: 'agreedPricePerServing', label: 'catalog:f.agreedPrice', width: 160, align: 'right', render: (v) => fmt.money(v) },
          { key: 'contactPerson', label: 'catalog:f.contactPerson', width: 160 },
          { key: 'phone', label: 'catalog:f.phone', width: 120, defaultHidden: true },
          { key: 'shifts', label: 'catalog:f.shifts', width: 120 },
        ]),
        fields: std.fields([
          { name: 'customerTypeId', label: 'catalog:f.customerType', type: 'lookup', lookup: 'customer-types' },
          { name: 'taxCode', label: 'catalog:f.taxCode', type: 'text', schema: z.string().regex(/^\d{10}(-\d{3})?$/, 'common:validation.number').optional().nullable() },
          { name: 'address', label: 'catalog:f.address', type: 'text', span: 24 },
          { name: 'provinceId', label: 'catalog:f.province', type: 'lookup', lookup: 'provinces' },
          { name: 'wardId', label: 'catalog:f.ward', type: 'lookup', lookup: 'wards' },
          { name: 'contactPerson', label: 'catalog:f.contactPerson', type: 'text', span: 8 },
          { name: 'phone', label: 'catalog:f.phone', type: 'text', span: 8 },
          { name: 'email', label: 'catalog:f.email', type: 'email', span: 8, schema: zs.email() },
          { name: 'defaultSubjectGroupId', label: 'catalog:f.defaultSubject', type: 'lookup', lookup: 'subject-groups' },
          { name: 'workerCount', label: 'catalog:f.workerCount', type: 'number', min: 0, span: 6 },
          { name: 'agreedPricePerServing', label: 'catalog:f.agreedPrice', type: 'money', min: 0, span: 6 },
          { name: 'shifts', label: 'catalog:f.shifts', type: 'text' },
          { name: 'allergens', label: 'catalog:f.allergens', type: 'text' },
          { name: 'note', label: 'catalog:f.note', type: 'textarea' },
        ]),
        filters: [{ name: 'customerTypeId', label: 'catalog:f.customerType', type: 'lookup', lookup: 'customer-types' }],
        detailTabs: (r) => [
          {
            key: 'contacts', label: t('tabs.contacts'),
            children: (
              <SubTable<Any>
                url={`/customers/${r.id}/contacts`} canEdit={canEdit} title={t('tabs.contacts')}
                columns={[
                  { title: t('sub.fullName'), dataIndex: 'fullName' }, { title: t('sub.position'), dataIndex: 'position' },
                  { title: t('f.phone'), dataIndex: 'phone' }, { title: t('f.email'), dataIndex: 'email' },
                  { title: t('sub.isPrimary'), dataIndex: 'isPrimary', render: (v) => (v ? <Tag color="green">✓</Tag> : null) },
                ]}
                fields={[
                  { name: 'fullName', label: 'catalog:sub.fullName', type: 'text', required: true }, { name: 'position', label: 'catalog:sub.position', type: 'text' },
                  { name: 'phone', label: 'catalog:f.phone', type: 'text' }, { name: 'email', label: 'catalog:f.email', type: 'email', schema: zs.email() },
                  { name: 'isPrimary', label: 'catalog:sub.isPrimary', type: 'switch' }, { name: 'note', label: 'catalog:f.note', type: 'textarea' },
                ]}
              />
            ),
          },
          {
            key: 'prices', label: t('tabs.prices'),
            children: (
              <SubTable<Any>
                url={`/customers/${r.id}/prices`} canEdit={canEdit} allowUpdate={false} title={t('tabs.prices')}
                columns={[
                  { title: t('sub.shift'), dataIndex: 'shift' }, { title: t('sub.subject'), dataIndex: 'subject' },
                  { title: t('sub.validFrom'), dataIndex: 'validFrom', render: (v) => fmt.date(v) }, { title: t('sub.validTo'), dataIndex: 'validTo', render: (v) => fmt.date(v) },
                  { title: t('sub.pricePerServing'), dataIndex: 'pricePerServing', align: 'right', render: (v) => fmt.money(v) },
                ]}
                fields={[
                  { name: 'mealShiftId', label: 'catalog:sub.shift', type: 'lookup', lookup: 'meal-shifts' }, { name: 'subjectGroupId', label: 'catalog:sub.subject', type: 'lookup', lookup: 'subject-groups' },
                  { name: 'validFrom', label: 'catalog:sub.validFrom', type: 'date', required: true }, { name: 'validTo', label: 'catalog:sub.validTo', type: 'date' },
                  { name: 'pricePerServing', label: 'catalog:sub.pricePerServing', type: 'money', required: true, min: 0 }, { name: 'note', label: 'catalog:f.note', type: 'textarea' },
                ]}
              />
            ),
          },
          { key: 'orders', label: t('tabs.orders'), children: <CustomerOrders id={r.id} /> },
          { key: 'menus', label: t('tabs.menus'), children: <CustomerMenus id={r.id} /> },
        ],
      }}
    />
  );
}
