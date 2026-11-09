import { fmt, parseVnNumber } from './format';

describe('Vietnamese formatting (SPEC §9)', () => {
  it('formats money as 1.234.567 ₫', () => {
    expect(fmt.money(1234567)).toBe('1.234.567 ₫');
    expect(fmt.money(1234567.6)).toBe('1.234.568 ₫');
  });
  it('formats decimals with comma', () => {
    expect(fmt.number(1234.5, 1)).toBe('1.234,5');
    expect(fmt.nutrient(12.345, 2)).toBe('12,35');
  });
  it('formats dates dd/MM/yyyy', () => {
    expect(fmt.date('2026-01-05')).toBe('05/01/2026');
  });
  it('parses Vietnamese numbers', () => {
    expect(parseVnNumber('1.234,5')).toBe(1234.5);
  });
  it('handles empty values', () => {
    expect(fmt.money(null)).toBe('');
    expect(fmt.date(undefined)).toBe('');
  });
});
