import { Descriptions, Drawer, Empty, Spin, Tag, Timeline, Typography } from 'antd';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { http } from '@/shared/api/http';
import { fmt } from '@/shared/utils/format';

interface HistoryItem { at: string; userName?: string; action: string; oldValues?: string | null; newValues?: string | null }

function parse(s?: string | null): Record<string, unknown> {
  if (!s) return {};
  try {
    return JSON.parse(s) as Record<string, unknown>;
  } catch {
    return {};
  }
}

function show(v: unknown) {
  if (v === null || v === undefined || v === '') return <Typography.Text type="secondary">∅</Typography.Text>;
  if (typeof v === 'boolean') return v ? '✓' : '✗';
  if (typeof v === 'object') return JSON.stringify(v);
  return String(v);
}

/** Change history from the centralized audit log, rendered as field-level diffs (SPEC §7 common features). */
export function HistoryDrawer({ resource, id, open, onClose }: { resource: string; id?: string; open: boolean; onClose: () => void }) {
  const { t } = useTranslation('catalog');
  const { data, isLoading } = useQuery({
    queryKey: ['history', resource, id],
    enabled: open && !!id,
    queryFn: async () => (await http.get<HistoryItem[]>(`/${resource}/${id}/history`)).data,
  });
  return (
    <Drawer open={open} onClose={onClose} title={t('common:actions.history')} width={Math.min(560, window.innerWidth)} destroyOnHidden>
      {isLoading ? (
        <Spin />
      ) : !data?.length ? (
        <Empty />
      ) : (
        <Timeline
          items={data.map((h) => {
            const o = parse(h.oldValues);
            const n = parse(h.newValues);
            const keys = Array.from(new Set([...Object.keys(n), ...Object.keys(o)])).filter((k) => !['id', 'createdAt', 'createdBy', 'isDeleted'].includes(k));
            return {
              color: h.action === 'Create' ? 'green' : h.action === 'Delete' ? 'red' : 'blue',
              children: (
                <div>
                  <Typography.Text strong>{fmt.dateTime(h.at)}</Typography.Text> <Tag>{t('history.' + h.action, h.action)}</Tag>
                  <Typography.Text type="secondary">{h.userName}</Typography.Text>
                  {keys.length > 0 && (
                    <Descriptions size="small" column={1} bordered style={{ marginTop: 8 }}>
                      {keys.map((k) => (
                        <Descriptions.Item key={k} label={k}>
                          {h.action === 'Update' ? (
                            <span>
                              {show(o[k])} → <b>{show(n[k])}</b>
                            </span>
                          ) : (
                            show(n[k] ?? o[k])
                          )}
                        </Descriptions.Item>
                      ))}
                    </Descriptions>
                  )}
                </div>
              ),
            };
          })}
        />
      )}
    </Drawer>
  );
}
