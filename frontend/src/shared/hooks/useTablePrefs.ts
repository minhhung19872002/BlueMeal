import { useEffect, useState } from 'react';
import { http } from '@/shared/api/http';

/** Persists per-user table column visibility on the server (SPEC §9 "lưu cấu hình cột theo user"). */
export function useTablePrefs(tableKey: string, defaultHidden: string[]) {
  const [hidden, setHidden] = useState<string[]>(defaultHidden);
  const [loaded, setLoaded] = useState(false);
  useEffect(() => {
    let cancelled = false;
    http
      .get<{ hidden?: string[] } | null>(`/profile/ui/table.${tableKey}`)
      .then((r) => {
        if (!cancelled && r.data?.hidden) setHidden(r.data.hidden);
      })
      .catch(() => undefined)
      .finally(() => !cancelled && setLoaded(true));
    return () => {
      cancelled = true;
    };
  }, [tableKey]);
  const update = (next: string[]) => {
    setHidden(next);
    void http.put(`/profile/ui/table.${tableKey}`, { hidden: next }).catch(() => undefined);
  };
  return { hidden, setHidden: update, loaded };
}

/** Warns before leaving a page or closing a form with unsaved changes (SPEC §9). */
export function useBeforeUnload(dirty: boolean) {
  useEffect(() => {
    if (!dirty) return;
    const h = (e: BeforeUnloadEvent) => {
      e.preventDefault();
      e.returnValue = '';
    };
    window.addEventListener('beforeunload', h);
    return () => window.removeEventListener('beforeunload', h);
  }, [dirty]);
}
