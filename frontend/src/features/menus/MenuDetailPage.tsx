import { useState } from 'react';
import { Alert, App, Button, Card, Col, DatePicker, Descriptions, Empty, Input, Modal, Row, Space, Table, Tag, Timeline, Typography } from 'antd';
import { CopyOutlined, EditOutlined, PrinterOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { useEnumLabel } from '@/shared/components/catalog/inputs';
import { NutritionPanel } from '@/shared/components/nutrition/NutritionPanel';
import { DATE_FORMAT, fmt, isoDate } from '@/shared/utils/format';
import { EDITABLE, MenuStatusTag, type MenuDetail } from './shared';

type Action = 'submit' | 'approve' | 'reject' | 'return' | 'publish' | 'cancel' | 'recall';

/** Menu detail with nutrition snapshot, approval history and workflow actions (SPEC §7.4.5). */
export default function MenuDetailPage() {
  const { id } = useParams();
  const { t, i18n } = useTranslation('menu');
  const { message } = App.useApp();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { has } = usePermission();
  const enumLabel = useEnumLabel();
  const [pending, setPending] = useState<Action | null>(null);
  const [comment, setComment] = useState('');
  const [copyOpen, setCopyOpen] = useState(false);
  const [copyDate, setCopyDate] = useState<dayjs.Dayjs | null>(dayjs().add(7, 'day'));
  const { data: m, isLoading } = useQuery({ queryKey: ['menu', id], queryFn: async () => (await http.get<MenuDetail>(`/menus/${id}`)).data });

  if (isLoading) return <Card loading />;
  if (!m) return <Empty />;

  const needComment = (a: Action) => a === 'reject' || a === 'return' || a === 'cancel' || a === 'recall' || (a === 'submit' && m.criticalWarnings > 0);
  const run = async (a: Action, c?: string) => {
    try {
      await http.post(`/menus/${m.id}/${a}`, a === 'publish' ? undefined : { comment: c || null });
      message.success(t(a));
      setPending(null);
      setComment('');
      await qc.invalidateQueries({ queryKey: ['menu', id] });
      void qc.invalidateQueries({ queryKey: ['menus'] });
    } catch (e) {
      message.error(problemMessage(e));
    }
  };
  const ask = (a: Action) => (needComment(a) || a === 'approve' ? setPending(a) : void run(a));
  const copy = async () => {
    try {
      const r = await http.post<{ id: string }>(`/menus/${m.id}/copy`, { menuDate: isoDate(copyDate) });
      setCopyOpen(false);
      navigate(`/menus/${r.data.id}/edit`);
    } catch (e) {
      message.error(problemMessage(e));
    }
  };

  const canApprove = has('menu.approve') || has('menu.approve_final');
  const buttons: { a: Action; show: boolean; danger?: boolean; primary?: boolean }[] = [
    { a: 'submit', show: EDITABLE.includes(m.status) && has('menu.submit'), primary: true },
    { a: 'approve', show: m.status === 'Submitted' && canApprove, primary: true },
    { a: 'return', show: m.status === 'Submitted' && canApprove },
    { a: 'reject', show: m.status === 'Submitted' && canApprove, danger: true },
    { a: 'publish', show: m.status === 'Approved' && has('menu.publish'), primary: true },
    { a: 'recall', show: (m.status === 'Approved' || m.status === 'Published') && has('menu.recall') },
    { a: 'cancel', show: m.status !== 'Cancelled' && m.status !== 'Published' && has('menu.cancel'), danger: true },
  ];
  const dishName = (r: { dishNameVi?: string; dishNameEn?: string }) => (i18n.language === 'en' && r.dishNameEn ? r.dishNameEn : r.dishNameVi);

  return (
    <Space direction="vertical" size="middle" style={{ width: '100%' }}>
      <Card>
        <div className="bm-page-header">
          <Space wrap>
            <Typography.Title level={4} style={{ margin: 0 }}>{t('detail')} {m.code}</Typography.Title>
            <MenuStatusTag status={m.status} />
            {m.status === 'Submitted' && <Tag>{t('approvalLevel')} {m.approvalLevel}</Tag>}
          </Space>
          <Space wrap className="bm-no-print">
            {EDITABLE.includes(m.status) && has('menu.edit') && <Button icon={<EditOutlined />} onClick={() => navigate(`/menus/${m.id}/edit`)} data-testid="menu-edit">{t('edit')}</Button>}
            {has('menu.create') && <Button icon={<CopyOutlined />} onClick={() => setCopyOpen(true)}>{t('copy')}</Button>}
            <Button icon={<PrinterOutlined />} onClick={() => window.print()}>{t('print')}</Button>
            {buttons.filter((b) => b.show).map((b) => (
              <Button key={b.a} type={b.primary ? 'primary' : 'default'} danger={b.danger} onClick={() => ask(b.a)} data-testid={`menu-${b.a}`}>{t(b.a)}</Button>
            ))}
          </Space>
        </div>
        <Descriptions size="small" column={{ xs: 1, sm: 2, lg: 4 }}>
          <Descriptions.Item label={t('date')}>{fmt.date(m.menuDate)}</Descriptions.Item>
          <Descriptions.Item label={t('shift')}>{i18n.language === 'en' && m.shiftEn ? m.shiftEn : m.shift}</Descriptions.Item>
          <Descriptions.Item label={t('servings')}>{fmt.number(m.servings)}</Descriptions.Item>
          <Descriptions.Item label={t('mode')}>{enumLabel('MenuCreationMode', m.creationMode)}</Descriptions.Item>
          <Descriptions.Item label={t('energy')}>{fmt.number(m.energyKcal, 0)}</Descriptions.Item>
          {m.costPerServing != null && <Descriptions.Item label={t('cost')}>{fmt.money(m.costPerServing)}</Descriptions.Item>}
          {m.budgetPerServing != null && <Descriptions.Item label={t('budget')}>{fmt.money(m.budgetPerServing)}</Descriptions.Item>}
          <Descriptions.Item label={t('score')}>{fmt.number(m.nutritionScore, 0)}</Descriptions.Item>
          {m.note && <Descriptions.Item label={t('note')}>{m.note}</Descriptions.Item>}
        </Descriptions>
        {m.criticalOverrideReason && <Alert type="warning" showIcon message={`${t('reason')}: ${m.criticalOverrideReason}`} style={{ marginTop: 8 }} />}
        {m.explanation && <Alert type="info" showIcon message={t('explanation')} description={i18n.language === 'en' ? m.explanation.en : m.explanation.vi} style={{ marginTop: 8 }} />}
      </Card>
      <Row gutter={[16, 16]}>
        <Col xs={24} lg={14}>
          <Card title={t('dishes')} size="small">
            <Table
              rowKey={(r) => r.id ?? r.dishId} dataSource={m.items} pagination={false} size="small"
              columns={[
                { title: t('slot'), dataIndex: 'dishType', width: 140, render: (v) => enumLabel('DishType', v) },
                { title: t('dishes'), key: 'n', render: (_, r) => <Space>{dishName(r)}<Typography.Text type="secondary">{r.dishCode}</Typography.Text></Space> },
                { title: t('factor'), dataIndex: 'servingFactor', align: 'right', width: 110, render: (v) => fmt.number(v, 2) },
                { title: t('locked'), dataIndex: 'recipeId', width: 160, render: (v) => (v ? <Tag color="blue">✓</Tag> : null) },
              ]}
            />
          </Card>
          <Card title={t('history')} size="small" style={{ marginTop: 16 }}>
            {m.approvals.length === 0 ? <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /> : (
              <Timeline items={m.approvals.map((a) => ({
                color: a.action === 'Reject' ? 'red' : a.action === 'Return' ? 'orange' : 'blue',
                children: (
                  <div>
                    <Typography.Text strong>{enumLabel('ApprovalAction', a.action)}</Typography.Text>{a.level > 0 && ` · L${a.level}`} · {a.userName} · {fmt.dateTime(a.at)}
                    <div><Typography.Text type="secondary">{enumLabel('MenuStatus', a.from)} → {enumLabel('MenuStatus', a.to)}</Typography.Text></div>
                    {a.comment && <div>{a.comment}</div>}
                  </div>
                ),
              }))} />
            )}
          </Card>
        </Col>
        <Col xs={24} lg={10}>
          <NutritionPanel nutrition={m.snapshot?.nutrition} cost={m.snapshot?.cost} warnings={m.snapshot?.warnings} />
        </Col>
      </Row>
      <Modal open={pending !== null} title={pending ? t(pending) : ''} onCancel={() => setPending(null)} okButtonProps={{ 'data-testid': 'menu-action-ok', disabled: !!pending && needComment(pending) && !comment.trim() } as never}
        onOk={() => pending && void run(pending, comment)} destroyOnHidden>
        {pending === 'submit' && m.criticalWarnings > 0 && <Alert type="error" showIcon message={t('criticalReason')} style={{ marginBottom: 8 }} />}
        {(pending === 'reject' || pending === 'return') && <Typography.Paragraph type="secondary">{t('commentRequired')}</Typography.Paragraph>}
        <Input.TextArea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} placeholder={pending && ['cancel', 'recall', 'submit'].includes(pending) ? t('reason') : t('comment')} maxLength={1000} data-testid="menu-comment" />
      </Modal>
      <Modal open={copyOpen} title={t('copy')} onCancel={() => setCopyOpen(false)} onOk={() => void copy()} okButtonProps={{ disabled: !copyDate }}>
        <DatePicker value={copyDate} onChange={setCopyDate} format={DATE_FORMAT} style={{ width: '100%' }} />
      </Modal>
    </Space>
  );
}
