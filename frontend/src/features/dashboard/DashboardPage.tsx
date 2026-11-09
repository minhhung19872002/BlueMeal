import { Card, Col, Empty, List, Row, Space, Statistic, Table, Tag, Typography } from 'antd';
import { AlertOutlined, AuditOutlined, ExperimentOutlined, FieldTimeOutlined, FileTextOutlined, FireOutlined, TeamOutlined, WalletOutlined } from '@ant-design/icons';
import { useQuery } from '@tanstack/react-query';
import { Bar, BarChart, CartesianGrid, ReferenceLine, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { useNavigate } from 'react-router';
import { useTranslation } from 'react-i18next';
import { http } from '@/shared/api/http';
import { useAuth } from '@/shared/auth/authStore';
import { useChartTheme } from '@/shared/components/nutrition/charts';
import { fmt } from '@/shared/utils/format';

interface Dashboard {
  pendingApproval?: number; drafts?: number; criticalAlerts?: number; latestAlerts?: { id: string; code: string; messageVi: string; messageEn: string; link?: string; createdAt: string }[];
  servingsToday?: number; servingsByShift?: { shift: string; servings: number; menus: number }[]; energyPct7d?: number | null; energyTrend?: { date: string; pct: number }[];
  costPerServingWeek?: number | null; budgetPerServingWeek?: number | null; expiringLots?: number;
  expiringList?: { id: string; ingredient: string; ingredientEn?: string; lotNo?: string; expiryDate: string; quantity: number; unit?: string }[]; samplesDue?: number;
}

/** Role-based home dashboard (SPEC §7.9): only widgets the user has permissions for are returned by the API. */
export default function DashboardPage() {
  const { t, i18n } = useTranslation('analysis');
  const navigate = useNavigate();
  const theme = useChartTheme();
  const user = useAuth((s) => s.user);
  const en = i18n.language === 'en';
  const { data, isLoading } = useQuery({ queryKey: ['dashboard'], queryFn: async () => (await http.get<Dashboard>('/dashboard')).data, refetchInterval: 60_000 });

  const tiles: { key: keyof Dashboard; title: string; value?: string | number | null; icon: React.ReactNode; to: string; danger?: boolean; extra?: string }[] = [];
  if (data?.pendingApproval !== undefined) tiles.push({ key: 'pendingApproval', title: t('dash.pendingApproval'), value: data.pendingApproval, icon: <AuditOutlined />, to: '/menus/approval' });
  if (data?.drafts !== undefined) tiles.push({ key: 'drafts', title: t('dash.drafts'), value: data.drafts, icon: <FileTextOutlined />, to: '/menus?status=Draft' });
  if (data?.criticalAlerts !== undefined) tiles.push({ key: 'criticalAlerts', title: t('dash.criticalAlerts'), value: data.criticalAlerts, icon: <AlertOutlined />, to: '/alerts', danger: (data.criticalAlerts ?? 0) > 0 });
  if (data?.servingsToday !== undefined) tiles.push({ key: 'servingsToday', title: t('dash.servingsToday'), value: fmt.number(data.servingsToday), icon: <TeamOutlined />, to: '/menus/published' });
  if (data && 'costPerServingWeek' in data) {
    const over = data.costPerServingWeek != null && data.budgetPerServingWeek != null && data.costPerServingWeek > data.budgetPerServingWeek;
    tiles.push({ key: 'costPerServingWeek', title: t('dash.costWeek'), value: data.costPerServingWeek == null ? '—' : fmt.money(data.costPerServingWeek), icon: <WalletOutlined />, to: '/cost-sheet', danger: over,
      extra: data.budgetPerServingWeek == null ? undefined : `${t('dash.budget')}: ${fmt.money(data.budgetPerServingWeek)} · ${over ? t('dash.overBudget') : t('dash.withinBudget')}` });
  }
  if (data && 'energyPct7d' in data) tiles.push({ key: 'energyPct7d', title: t('dash.energy7d'), value: data.energyPct7d == null ? '—' : fmt.percent(data.energyPct7d), icon: <FireOutlined />, to: '/analysis/nutrition' });
  if (data?.expiringLots !== undefined) tiles.push({ key: 'expiringLots', title: t('dash.expiringLots'), value: data.expiringLots, icon: <FieldTimeOutlined />, to: '/stock/balance', danger: (data.expiringLots ?? 0) > 0 });
  if (data?.samplesDue !== undefined) tiles.push({ key: 'samplesDue', title: t('dash.samplesDue'), value: data.samplesDue, icon: <ExperimentOutlined />, to: '/safety/samples?state=due', danger: (data.samplesDue ?? 0) > 0 });

  return (
    <div>
      <Typography.Title level={4}>{t('dashboard:welcome', { name: user?.fullName })}</Typography.Title>
      <Row gutter={[16, 16]}>
        {tiles.map((x) => (
          <Col xs={12} md={8} xl={6} key={x.key}>
            <Card hoverable onClick={() => navigate(x.to)} loading={isLoading} data-testid={`dash-${x.key}`}>
              <Statistic title={<Space>{x.icon}{x.title}</Space>} value={x.value ?? 0} valueStyle={x.danger ? { color: '#d03b3b' } : undefined} />
              {x.extra && <Typography.Text type="secondary" style={{ fontSize: 12 }}>{x.extra}</Typography.Text>}
            </Card>
          </Col>
        ))}
        {!isLoading && tiles.length === 0 && <Col xs={24}><Card>{t('dashboard:intro')}</Card></Col>}
      </Row>
      <Row gutter={[16, 16]} style={{ marginTop: 16 }}>
        {data?.energyTrend && (
          <Col xs={24} lg={12}>
            <Card title={t('dash.energyTrend')} size="small">
              {data.energyTrend.length === 0 ? <Empty description={t('dash.noData')} /> : (
                <ResponsiveContainer width="100%" height={220}>
                  <BarChart data={data.energyTrend.map((p) => ({ ...p, label: fmt.date(p.date).slice(0, 5) }))}>
                    <CartesianGrid vertical={false} stroke={theme.grid} />
                    <XAxis dataKey="label" stroke={theme.textSecondary} fontSize={12} />
                    <YAxis unit="%" stroke={theme.textSecondary} fontSize={12} domain={[0, 'dataMax + 10']} />
                    <Tooltip formatter={(v) => fmt.percent(Number(v))} />
                    <ReferenceLine y={100} stroke={theme.textSecondary} strokeDasharray="4 4" />
                    <Bar dataKey="pct" name={t('dash.energy7d')} fill={theme.series[0]} radius={[4, 4, 0, 0]} barSize={28} />
                  </BarChart>
                </ResponsiveContainer>
              )}
            </Card>
          </Col>
        )}
        {data?.servingsByShift && (
          <Col xs={24} lg={12}>
            <Card title={t('dash.byShift')} size="small">
              <Table rowKey="shift" dataSource={data.servingsByShift} pagination={false} size="small" locale={{ emptyText: t('dash.noData') }}
                columns={[{ title: t('dash.shift'), dataIndex: 'shift' }, { title: t('dash.menus'), dataIndex: 'menus', align: 'right' }, { title: t('dash.servings'), dataIndex: 'servings', align: 'right', render: (v) => fmt.number(v) }]} />
            </Card>
          </Col>
        )}
        {data?.latestAlerts && (
          <Col xs={24} lg={12}>
            <Card title={t('dash.latestAlerts')} size="small" extra={<a onClick={() => navigate('/alerts')}>{t('alerts.title')}</a>}>
              <List size="small" dataSource={data.latestAlerts} locale={{ emptyText: t('dash.noData') }}
                renderItem={(a) => (
                  <List.Item onClick={() => a.link && navigate(a.link)} style={{ cursor: a.link ? 'pointer' : undefined }}>
                    <Space direction="vertical" size={0}>
                      <Space size={4}><Tag color="red">{a.code}</Tag><Typography.Text type="secondary" style={{ fontSize: 12 }}>{fmt.dateTime(a.createdAt)}</Typography.Text></Space>
                      <Typography.Text>{en ? a.messageEn : a.messageVi}</Typography.Text>
                    </Space>
                  </List.Item>
                )} />
            </Card>
          </Col>
        )}
        {data?.expiringList && (
          <Col xs={24} lg={12}>
            <Card title={t('dash.expiringList')} size="small" extra={<a onClick={() => navigate('/stock/balance')}>{t('dash.expiringLots')}</a>}>
              <Table rowKey="id" dataSource={data.expiringList} pagination={false} size="small" locale={{ emptyText: t('dash.noData') }}
                columns={[
                  { title: t('dash.ingredient'), key: 'i', render: (_, l) => (en && l.ingredientEn ? l.ingredientEn : l.ingredient) },
                  { title: t('dash.lot'), dataIndex: 'lotNo', width: 100 },
                  { title: t('dash.expiry'), dataIndex: 'expiryDate', width: 110, render: fmt.date },
                  { title: t('dash.quantity'), key: 'q', width: 110, align: 'right', render: (_, l) => `${fmt.number(l.quantity, 3)} ${l.unit ?? ''}` },
                ]} />
            </Card>
          </Col>
        )}
      </Row>
    </div>
  );
}
