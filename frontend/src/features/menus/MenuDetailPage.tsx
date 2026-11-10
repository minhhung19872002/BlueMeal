import { useState } from 'react';
import { App, Button, Card, DatePicker, Empty, Input, Modal, Table, Tag, Typography } from 'antd';
import { CopyOutlined, EditOutlined, PrinterOutlined } from '@ant-design/icons';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import dayjs from 'dayjs';
import { useNavigate, useParams } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http, problemMessage } from '@/shared/api/http';
import { usePermission } from '@/shared/auth/authStore';
import { useEnumLabel } from '@/shared/components/catalog/inputs';
import { NutritionPanel } from '@/shared/components/nutrition/NutritionPanel';
import { InfoGrid, Page, Section } from '@/shared/components/PageChrome';
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
  const dot = (action: string) => (action === 'Reject' ? 'bm-hist-dot is-red' : action === 'Return' ? 'bm-hist-dot is-orange' : action === 'Approve' || action === 'Publish' ? 'bm-hist-dot is-green' : 'bm-hist-dot');
  const info = [
    { key: 'd', label: t('date'), value: fmt.date(m.menuDate) },
    { key: 's', label: t('shift'), value: i18n.language === 'en' && m.shiftEn ? m.shiftEn : m.shift },
    { key: 'n', label: t('servings'), value: fmt.number(m.servings) },
    { key: 'm', label: t('mode'), value: enumLabel('MenuCreationMode', m.creationMode) },
    { key: 'e', label: t('energy'), value: fmt.number(m.energyKcal, 0) },
    ...(m.costPerServing != null ? [{ key: 'c', label: t('cost'), value: fmt.money(m.costPerServing) }] : []),
    ...(m.budgetPerServing != null ? [{ key: 'b', label: t('budget'), value: fmt.money(m.budgetPerServing) }] : []),
    { key: 'sc', label: t('score'), value: fmt.number(m.nutritionScore, 0) },
  ];

  return (
    <Page
      title={`${t('detail')} ${m.code}`}
      tag={<><MenuStatusTag status={m.status} />{m.status === 'Submitted' && <Tag>{t('approvalLevel')} {m.approvalLevel}</Tag>}</>}
      actions={(
        <>
          <Button onClick={() => navigate('/menus')}>{t('common:actions.back')}</Button>
          {EDITABLE.includes(m.status) && has('menu.edit') && <Button icon={<EditOutlined />} onClick={() => navigate(`/menus/${m.id}/edit`)} data-testid="menu-edit">{t('edit')}</Button>}
          {has('menu.create') && <Button icon={<CopyOutlined />} onClick={() => setCopyOpen(true)}>{t('copy')}</Button>}
          <Button icon={<PrinterOutlined />} onClick={() => window.print()}>{t('print')}</Button>
          {buttons.filter((b) => b.show).map((b) => (
            <Button key={b.a} type={b.primary ? 'primary' : 'default'} danger={b.danger} onClick={() => ask(b.a)} data-testid={`menu-${b.a}`}>{t(b.a)}</Button>
          ))}
        </>
      )}
    >
      <div className="bm-2col">
        <div className="bm-2col-main">
          <div className="bm-card bm-pad bm-stack">
            <InfoGrid items={info} />
            {m.note && <div className="bm-info-l">{t('note')}: <span style={{ color: 'var(--ink)' }}>{m.note}</span></div>}
            {m.criticalOverrideReason && <div className="bm-note is-orange">{t('reason')}: {m.criticalOverrideReason}</div>}
            {m.explanation && <div className="bm-note is-info"><b>{t('explanation')}:</b> {i18n.language === 'en' ? m.explanation.en : m.explanation.vi}</div>}
          </div>
          <Section title={t('dishes')}>
            <Table
              rowKey={(r) => r.id ?? r.dishId} dataSource={m.items} pagination={false} scroll={{ x: 'max-content' }}
              columns={[
                { title: t('slot'), dataIndex: 'dishType', width: 140, render: (v) => enumLabel('DishType', v) },
                { title: t('dishes'), key: 'n', render: (_, r) => <span><span style={{ fontWeight: 500 }}>{dishName(r)}</span> <span style={{ color: 'var(--ink-3)', fontSize: 12 }}>{r.dishCode}</span></span> },
                { title: t('factor'), dataIndex: 'servingFactor', align: 'right', width: 110, render: (v) => fmt.number(v, 2) },
                { title: t('locked'), dataIndex: 'recipeId', width: 140, render: (v) => (v ? <Tag color="blue">✓</Tag> : null) },
              ]}
            />
          </Section>
          <Section title={t('history')}>
            {m.approvals.length === 0 ? <div className="bm-pad"><Empty image={Empty.PRESENTED_IMAGE_SIMPLE} /></div> : (
              <div className="bm-hist">
                {m.approvals.map((a) => (
                  <div key={a.id} className="bm-hist-i">
                    <span className={dot(a.action)} />
                    <div>
                      <div><b>{enumLabel('ApprovalAction', a.action)}</b>{a.level > 0 && ` · L${a.level}`} · {a.userName} · <span style={{ fontVariantNumeric: 'tabular-nums' }}>{fmt.dateTime(a.at)}</span></div>
                      <div className="bm-hist-flow">{enumLabel('MenuStatus', a.from)} → {enumLabel('MenuStatus', a.to)}</div>
                      {a.comment && <div className="bm-hist-c">{a.comment}</div>}
                    </div>
                  </div>
                ))}
              </div>
            )}
          </Section>
        </div>
        <div className="bm-2col-side">
          <NutritionPanel nutrition={m.snapshot?.nutrition} cost={m.snapshot?.cost} warnings={m.snapshot?.warnings} />
        </div>
      </div>
      <Modal open={pending !== null} title={pending ? t(pending) : ''} onCancel={() => setPending(null)} okButtonProps={{ 'data-testid': 'menu-action-ok', disabled: !!pending && needComment(pending) && !comment.trim() } as never}
        onOk={() => pending && void run(pending, comment)} destroyOnHidden width={460}>
        {pending === 'submit' && m.criticalWarnings > 0 && <div className="bm-note is-red" style={{ marginBottom: 8 }}>{t('criticalReason')}</div>}
        {(pending === 'reject' || pending === 'return') && <Typography.Paragraph type="secondary">{t('commentRequired')}</Typography.Paragraph>}
        <Input.TextArea rows={3} value={comment} onChange={(e) => setComment(e.target.value)} placeholder={pending && ['cancel', 'recall', 'submit'].includes(pending) ? t('reason') : t('comment')} maxLength={1000} data-testid="menu-comment" />
      </Modal>
      <Modal open={copyOpen} title={t('copy')} onCancel={() => setCopyOpen(false)} onOk={() => void copy()} okButtonProps={{ disabled: !copyDate }} width={460}>
        <DatePicker value={copyDate} onChange={setCopyDate} format={DATE_FORMAT} style={{ width: '100%' }} />
      </Modal>
    </Page>
  );
}
