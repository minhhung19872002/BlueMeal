import type { CSSProperties, ReactNode } from 'react';

/**
 * Page chrome from Claude Design BMMenus / BMOps (project ae7386cc…): H1 + status tag + action buttons above the content,
 * then cards. Classes live in app/layout/pages.css and pages-menus-ops.css.
 */
export function Page({ title, tag, actions, sub, children, className }: { title: ReactNode; tag?: ReactNode; actions?: ReactNode; sub?: ReactNode; children?: ReactNode; className?: string }) {
  return (
    <div className={className ? `bm-page ${className}` : 'bm-page'}>
      <div className="bm-ph">
        <div className="bm-ph-title">
          <h1 className="bm-h1">{title}</h1>
          {tag}
        </div>
        {actions && <div className="bm-ph-actions bm-no-print">{actions}</div>}
      </div>
      {sub && <div className="bm-sub">{sub}</div>}
      {children}
    </div>
  );
}

/** Card with an optional 14×16 header (title, hint, extra on the right). */
export function Section({ title, hint, extra, children, className, bodyClassName, style }: {
  title?: ReactNode; hint?: ReactNode; extra?: ReactNode; children?: ReactNode; className?: string; bodyClassName?: string; style?: CSSProperties;
}) {
  return (
    <section className={className ? `bm-card ${className}` : 'bm-card'} style={style}>
      {(title || extra) && (
        <div className="bm-sec-head">
          <div>
            {title && <div className="bm-sec-title">{title}</div>}
            {hint && <div className="bm-sec-hint">{hint}</div>}
          </div>
          {extra && <div className="bm-sec-extra">{extra}</div>}
        </div>
      )}
      {bodyClassName ? <div className={bodyClassName}>{children}</div> : children}
    </section>
  );
}

/** Label / value grid (design "info" block). */
export function InfoGrid({ items }: { items: { label: ReactNode; value: ReactNode; key?: string }[] }) {
  return (
    <div className="bm-info">
      {items.map((i, n) => (
        <div key={i.key ?? n} className="bm-info-i">
          <div className="bm-info-l">{i.label}</div>
          <div className="bm-info-v">{i.value}</div>
        </div>
      ))}
    </div>
  );
}

/** One document line rendered as labelled cells that wrap on small screens (design BMOps "lines"). */
export function LineRow({ children, actions }: { children: ReactNode; actions?: ReactNode }) {
  return (
    <div className="bm-line">
      {children}
      {actions && <div className="bm-line-acts">{actions}</div>}
    </div>
  );
}

export function LineCell({ label, flex, children, static: isStatic, color }: { label: ReactNode; flex: string; children: ReactNode; static?: boolean; color?: string }) {
  return (
    <div className="bm-lcell" style={{ flex }}>
      <span className="bm-lcell-l">{label}</span>
      {isStatic ? <div className="bm-lcell-static" style={color ? { color } : undefined}>{children}</div> : children}
    </div>
  );
}

export function Totals({ items }: { items: { label: ReactNode; value: ReactNode }[] }) {
  return (
    <div className="bm-totals">
      {items.map((t, i) => <span key={i}>{t.label} <b>{t.value}</b></span>)}
    </div>
  );
}
