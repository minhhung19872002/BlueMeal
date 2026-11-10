import type { ReactNode } from 'react';

/**
 * Small presentational pieces of Claude Design BMAnalysis / BMAdmin (styles in pages-analysis-admin.css).
 * They carry no data logic: pages pass real API data in.
 */

type Kind = 'default' | 'soft' | 'primary' | 'danger';
const KIND: Record<Kind, string> = { default: 'bm-x-btn', soft: 'bm-x-btn k-soft', primary: 'bm-x-btn k-primary', danger: 'bm-x-btn k-danger' };

export function XButton({ kind = 'default', children, ...rest }: { kind?: Kind; children: ReactNode } & React.ButtonHTMLAttributes<HTMLButtonElement>) {
  return <button type="button" className={KIND[kind]} {...rest}>{children}</button>;
}

/** Page header: H1 + action buttons on the right, optional sub-line. */
export function PageHead({ title, actions, sub }: { title: ReactNode; actions?: ReactNode; sub?: ReactNode }) {
  return (
    <>
      <div className="bm-ph">
        <h1 className="bm-h1">{title}</h1>
        {actions && <div className="bm-ph-actions">{actions}</div>}
      </div>
      {sub && <div className="bm-x-note">{sub}</div>}
    </>
  );
}

export interface PillTab { key: string; label: ReactNode; testId?: string }

/** Pill tabs (role="tablist" so tests and screen readers see real tabs). */
export function PillTabs({ items, value, onChange, label }: { items: PillTab[]; value: string; onChange: (k: string) => void; label?: string }) {
  return (
    <div className="bm-x-tabs" role="tablist" aria-label={label}>
      {items.map((i) => (
        <button key={i.key} type="button" role="tab" aria-selected={value === i.key} className={value === i.key ? 'bm-x-tab is-on' : 'bm-x-tab'} onClick={() => onChange(i.key)} data-testid={i.testId}>
          {i.label}
        </button>
      ))}
    </div>
  );
}

/** Config section card: title + hint, body, optional footer (buttons right). */
export function Section({ title, hint, extra, children, foot, testId, bodyClass = 'bm-x-sec-body' }: { title?: ReactNode; hint?: ReactNode; extra?: ReactNode; children?: ReactNode; foot?: ReactNode; testId?: string; bodyClass?: string }) {
  return (
    <section className="bm-x-sec" data-testid={testId}>
      {(title || extra) && (
        <div className="bm-x-sec-head">
          <div>
            {title && <div className="bm-x-sec-title">{title}</div>}
            {hint && <div className="bm-x-sec-hint">{hint}</div>}
          </div>
          {extra}
        </div>
      )}
      {children !== undefined && children !== null && children !== false && <div className={bodyClass}>{children}</div>}
      {foot && <div className="bm-x-sec-foot">{foot}</div>}
    </section>
  );
}

/** Read-only field (label + value) inside a section grid. */
export function StaticField({ label, value, help }: { label: ReactNode; value: ReactNode; help?: ReactNode }) {
  return (
    <div className="bm-x-field">
      <span className="bm-x-field-l">{label}</span>
      <div className="bm-x-field-v">{value}</div>
      {help && <span className="bm-x-field-h">{help}</span>}
    </div>
  );
}

/** 44×24 switch with "on/off" text; works as an antd Form control (checked/onChange). */
export function Toggle({ checked, onChange, disabled, onText, offText, label, testId }: { checked?: boolean; onChange?: (v: boolean) => void; disabled?: boolean; onText: string; offText: string; label?: string; testId?: string }) {
  return (
    <button type="button" role="switch" aria-checked={!!checked} aria-label={label} disabled={disabled} className={checked ? 'bm-x-switch is-on' : 'bm-x-switch'} onClick={() => onChange?.(!checked)} data-testid={testId}>
      <span className="bm-x-track"><span className="bm-x-knob" /></span>
      {checked ? onText : offText}
    </button>
  );
}

/** Labelled filter control in the analysis filter card. */
export function FLabel({ label, wide, children }: { label: ReactNode; wide?: boolean; children: ReactNode }) {
  return (
    <label className={wide ? 'bm-x-flabel is-wide' : 'bm-x-flabel'}>
      {label}
      {children}
    </label>
  );
}

export function Tile({ label, value, extra, bad, onClick, testId }: { label: ReactNode; value: ReactNode; extra?: ReactNode; bad?: boolean; onClick?: () => void; testId?: string }) {
  const cls = bad ? 'bm-x-tile is-bad' : 'bm-x-tile';
  const inner = (
    <>
      <span className="bm-x-tile-l">{label}</span>
      <span className="bm-x-tile-v">{value}</span>
      <span className="bm-x-tile-e">{extra}</span>
    </>
  );
  return onClick ? <button type="button" className={cls} onClick={onClick} data-testid={testId}>{inner}</button> : <div className={cls} data-testid={testId}>{inner}</div>;
}

export interface LegendItem { name: string; color: string }

export function Panel({ title, legend, full, extra, children, testId }: { title: ReactNode; legend?: LegendItem[]; full?: boolean; extra?: ReactNode; children: ReactNode; testId?: string }) {
  return (
    <section className={full ? 'bm-x-panel is-full' : 'bm-x-panel'} data-testid={testId}>
      <div className="bm-x-panel-head">
        <h2 className="bm-x-panel-title">{title}</h2>
        {legend && legend.length > 0 && (
          <div className="bm-x-legend">{legend.map((g) => <span key={g.name}><i style={{ background: g.color }} />{g.name}</span>)}</div>
        )}
        {extra}
      </div>
      {children}
    </section>
  );
}

export interface BarItem { key: string; label: string; text?: string; value: number; value2?: number; color?: string }

/** Vertical bars (one or two series) with an optional dashed reference line, scaled to `max`. */
export function Bars({ items, max, refValue, refLabel, minWidth = 280 }: { items: BarItem[]; max: number; refValue?: number; refLabel?: string; minWidth?: number }) {
  const pct = (v: number) => `${Math.max(0, Math.min(100, (v / max) * 100))}%`;
  const refBottom = refValue != null ? `${(refValue / max) * 82}%` : undefined;
  return (
    <div className="bm-x-bars-wrap">
      <div style={{ minWidth }}>
        <div className="bm-x-bars">
          {refBottom && (
            <>
              <div className="bm-x-ref" style={{ bottom: refBottom }} />
              <span className="bm-x-ref-l" style={{ bottom: `calc(${refBottom} - 8px)` }}>{refLabel}</span>
            </>
          )}
          {items.map((b) => (
            <div key={b.key} className="bm-x-bar">
              <span className="bm-x-bar-t">{b.text}</span>
              <div className="bm-x-bar-pair">
                <div className="bm-x-bar-f" style={{ height: pct(b.value), background: b.color ?? 'var(--primary)' }} />
                {b.value2 != null && <div className="bm-x-bar-f" style={{ height: pct(b.value2), background: 'var(--info)' }} />}
              </div>
            </div>
          ))}
        </div>
        <div className="bm-x-bars-x">{items.map((b) => <span key={b.key}>{b.label}</span>)}</div>
      </div>
    </div>
  );
}

export interface DonutPart { name: string; color: string; pct: number; text: string; note?: string }

export function Donut({ parts, label, center }: { parts: DonutPart[]; label: string; center: string }) {
  let acc = 0;
  const stops = parts.map((p) => { const from = acc; acc += p.pct; return `${p.color} ${from}% ${acc}%`; });
  return (
    <div className="bm-x-donut-wrap">
      <div className="bm-x-donut" role="img" aria-label={label} style={{ background: `conic-gradient(${stops.join(', ')}${acc < 100 ? `, var(--surface-2) ${acc}% 100%` : ''})` }}>
        <div className="bm-x-donut-hole">{center}</div>
      </div>
      <div className="bm-x-parts">
        {parts.map((p) => <div key={p.name} className="bm-x-part"><i style={{ background: p.color }} /><span>{p.name}</span><b>{p.text}</b>{p.note && <small>{p.note}</small>}</div>)}
      </div>
    </div>
  );
}

export interface HBarItem { key: string; name: string; text: string; width: number; tone: 'good' | 'low' | 'high'; status: string }

export function HBars({ items }: { items: HBarItem[] }) {
  return (
    <div className="bm-x-hbars">
      {items.map((b) => (
        <div key={b.key} className="bm-x-hbar">
          <div className="bm-x-hbar-top"><span>{b.name}</span><span>{b.text} · <b className={`c-${b.tone}`}>{b.status}</b></span></div>
          <div className="bm-x-hbar-track"><div className={`bg-${b.tone}`} style={{ width: `${Math.max(0, Math.min(100, b.width))}%` }} /></div>
        </div>
      ))}
    </div>
  );
}
