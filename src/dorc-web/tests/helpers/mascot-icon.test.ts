import { describe, expect, it } from 'vitest';
import {
  getEasterSundayUtc,
  getMascotIconPath
} from '../../src/helpers/mascot-icon';

describe('mascot-icon helper', () => {
  it('computes Easter Sunday accurately across years', () => {
    expect(getEasterSundayUtc(2025).toISOString().slice(0, 10)).toBe(
      '2025-04-20'
    );
    expect(getEasterSundayUtc(2026).toISOString().slice(0, 10)).toBe(
      '2026-04-05'
    );
    expect(getEasterSundayUtc(2027).toISOString().slice(0, 10)).toBe(
      '2027-03-28'
    );
  });

  it('returns the Christmas mascot throughout December', () => {
    expect(getMascotIconPath(new Date(2026, 11, 1))).toBe(
      '/modern-dorc-christmas.webp'
    );
    expect(getMascotIconPath(new Date(2026, 11, 25))).toBe(
      '/modern-dorc-christmas.webp'
    );
    expect(getMascotIconPath(new Date(2026, 11, 31))).toBe(
      '/modern-dorc-christmas.webp'
    );
  });

  it('returns the Easter mascot during Easter week (Palm Sunday through Easter Tuesday)', () => {
    // In 2026, Easter Sunday is April 5 (month index 3, day 5)
    expect(getMascotIconPath(new Date(2026, 2, 28))).toBe('/modern-dorc.webp');
    expect(getMascotIconPath(new Date(2026, 2, 29))).toBe(
      '/modern-dorc-easter.webp'
    );
    expect(getMascotIconPath(new Date(2026, 3, 3))).toBe(
      '/modern-dorc-easter.webp'
    );
    expect(getMascotIconPath(new Date(2026, 3, 5))).toBe(
      '/modern-dorc-easter.webp'
    );
    expect(getMascotIconPath(new Date(2026, 3, 6))).toBe(
      '/modern-dorc-easter.webp'
    );
    expect(getMascotIconPath(new Date(2026, 3, 7))).toBe(
      '/modern-dorc-easter.webp'
    );
    expect(getMascotIconPath(new Date(2026, 3, 8))).toBe('/modern-dorc.webp');
  });

  it('returns the Summer mascot during June, July, and August', () => {
    expect(getMascotIconPath(new Date(2026, 4, 31))).toBe('/modern-dorc.webp');
    expect(getMascotIconPath(new Date(2026, 5, 1))).toBe(
      '/modern-dorc-summer.webp'
    );
    expect(getMascotIconPath(new Date(2026, 6, 15))).toBe(
      '/modern-dorc-summer.webp'
    );
    expect(getMascotIconPath(new Date(2026, 7, 31))).toBe(
      '/modern-dorc-summer.webp'
    );
    expect(getMascotIconPath(new Date(2026, 8, 1))).toBe('/modern-dorc.webp');
  });

  it('returns the default mascot outside holiday windows', () => {
    expect(getMascotIconPath(new Date(2026, 0, 15))).toBe('/modern-dorc.webp');
    expect(getMascotIconPath(new Date(2026, 9, 20))).toBe('/modern-dorc.webp');
    expect(getMascotIconPath(new Date(2026, 10, 30))).toBe('/modern-dorc.webp');
  });
});
