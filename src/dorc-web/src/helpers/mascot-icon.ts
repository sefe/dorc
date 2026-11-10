const DEFAULT_MASCOT_ICON = '/modern-dorc.webp';
const CHRISTMAS_MASCOT_ICON = '/modern-dorc-christmas.webp';
const EASTER_MASCOT_ICON = '/modern-dorc-easter.webp';
const SUMMER_MASCOT_ICON = '/modern-dorc-summer.webp';

const MS_PER_DAY = 24 * 60 * 60 * 1000;

/**
 * Calculates Easter Sunday for the given Gregorian year (Anonymous Gregorian algorithm).
 * Returns a UTC midnight Date for day-difference comparisons.
 */
export function getEasterSundayUtc(year: number): Date {
  const a = year % 19;
  const b = Math.floor(year / 100);
  const c = year % 100;
  const d = Math.floor(b / 4);
  const e = b % 4;
  const f = Math.floor((b + 8) / 25);
  const g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30;
  const i = Math.floor(c / 4);
  const k = c % 4;
  const l = (32 + 2 * e + 2 * i - h - k) % 7;
  const m = Math.floor((a + 11 * h + 22 * l) / 451);
  const month = Math.floor((h + l - 7 * m + 114) / 31) - 1;
  const day = ((h + l - 7 * m + 114) % 31) + 1;
  return new Date(Date.UTC(year, month, day));
}

/**
 * Returns the seasonal DOrc mascot icon path for the given date:
 * - Christmas (`/modern-dorc-christmas.webp`): December (Dec 1 – Dec 31)
 * - Easter (`/modern-dorc-easter.webp`): Palm Sunday through Easter Tuesday (Easter - 7 days to Easter + 2 days)
 * - Summer (`/modern-dorc-summer.webp`): Meteorological summer (June 1 – August 31)
 * - Default (`/modern-dorc.webp`): All other dates
 */
export function getMascotIconPath(date: Date = new Date()): string {
  const year = date.getFullYear();
  const month = date.getMonth();
  const day = date.getDate();

  if (month === 11) {
    return CHRISTMAS_MASCOT_ICON;
  }

  const currentUtc = Date.UTC(year, month, day);
  const easterUtc = getEasterSundayUtc(year).getTime();
  const daysFromEaster = Math.round((currentUtc - easterUtc) / MS_PER_DAY);
  if (daysFromEaster >= -7 && daysFromEaster <= 2) {
    return EASTER_MASCOT_ICON;
  }

  if (month >= 5 && month <= 7) {
    return SUMMER_MASCOT_ICON;
  }

  return DEFAULT_MASCOT_ICON;
}
