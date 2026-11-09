import dayjs from 'dayjs';

/** Vietnamese number formatting (SPEC §9): 1.234.567 ₫, dates dd/MM/yyyy. */
const cache = new Map<number, Intl.NumberFormat>();
const vnNumber = (digits: number) => {
  let f = cache.get(digits);
  if (!f) {
    f = new Intl.NumberFormat('vi-VN', { minimumFractionDigits: digits, maximumFractionDigits: digits });
    cache.set(digits, f);
  }
  return f;
};

export const fmt = {
  number(v: number | null | undefined, digits = 0): string {
    if (v === null || v === undefined || Number.isNaN(v)) return '';
    return vnNumber(digits).format(v);
  },
  money(v: number | null | undefined): string {
    if (v === null || v === undefined || Number.isNaN(v)) return '';
    return vnNumber(0).format(Math.round(v)) + ' ₫';
  },
  percent(v: number | null | undefined, digits = 1): string {
    if (v === null || v === undefined || Number.isNaN(v)) return '';
    return vnNumber(digits).format(v) + '%';
  },
  date(v: string | Date | null | undefined): string {
    return v ? dayjs(v).format('DD/MM/YYYY') : '';
  },
  dateTime(v: string | Date | null | undefined): string {
    return v ? dayjs(v).format('DD/MM/YYYY HH:mm') : '';
  },
  /** Localized weekday name (dayjs locale follows the UI language). */
  weekday(v: string | dayjs.Dayjs, short = false): string {
    return dayjs(v).format(short ? 'dd' : 'dddd');
  },
  time(v: string | null | undefined): string {
    return v ? v.slice(0, 5) : '';
  },
  /** Display rounding per SPEC §6.1: energy 0, macro 1, micro 2 decimals. */
  nutrient(v: number | null | undefined, decimals: number): string {
    return fmt.number(v, decimals);
  },
};

export const DATE_FORMAT = 'DD/MM/YYYY';
export const isoDate = (d: dayjs.Dayjs | null | undefined) => (d ? d.format('YYYY-MM-DD') : undefined);

/** Parses a Vietnamese formatted number ("1.234,5") back to a number. */
export function parseVnNumber(s: string | undefined): number {
  if (!s) return 0;
  return Number(s.replace(/[^\d,-]/g, '').replace(',', '.')) || 0;
}

/** Monday of the week containing d, independent of the dayjs locale's week start. */
export const mondayOf = (d: dayjs.Dayjs) => d.subtract((d.day() + 6) % 7, 'day').startOf('day');
