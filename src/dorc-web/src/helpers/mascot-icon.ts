const DEFAULT_DEPLOY_MASCOT_ICON = '/modern-dorc-deploy.webp';
const CHRISTMAS_DEPLOY_MASCOT_ICON = '/modern-dorc-deploy-christmas.webp';
const EASTER_DEPLOY_MASCOT_ICON = '/modern-dorc-deploy-easter.webp';
const SUMMER_DEPLOY_MASCOT_ICON = '/modern-dorc-deploy-summer.webp';
const HALLOWEEN_DEPLOY_MASCOT_ICON = '/modern-dorc-deploy-halloween.webp';

export type MascotSeason =
  'christmas' | 'halloween' | 'easter' | 'summer' | 'default';

export type MascotTheme = 'auto' | MascotSeason;

export const DEPLOY_MASCOT_THEMES: ReadonlyArray<{
  id: MascotTheme;
  label: string;
}> = [
  { id: 'auto', label: 'Auto' },
  { id: 'default', label: 'Default' },
  { id: 'christmas', label: 'Christmas' },
  { id: 'halloween', label: 'Halloween' },
  { id: 'easter', label: 'Easter' },
  { id: 'summer', label: 'Summer' }
];

const DEPLOY_MASCOT_THEME_STORAGE_KEY = 'dorc-deploy-mascot-theme';

const VALID_THEMES = new Set<string>(DEPLOY_MASCOT_THEMES.map(t => t.id));

const MS_PER_DAY = 24 * 60 * 60 * 1000;

export function getDeployMascotThemeOverride(): MascotTheme {
  try {
    const stored = window.localStorage?.getItem(
      DEPLOY_MASCOT_THEME_STORAGE_KEY
    );
    if (stored && VALID_THEMES.has(stored)) {
      return stored as MascotTheme;
    }
  } catch {
    // Ignore storage access errors
  }
  return 'auto';
}

export function setDeployMascotThemeOverride(theme: MascotTheme): void {
  try {
    if (theme === 'auto') {
      window.localStorage?.removeItem(DEPLOY_MASCOT_THEME_STORAGE_KEY);
    } else {
      window.localStorage?.setItem(DEPLOY_MASCOT_THEME_STORAGE_KEY, theme);
    }
  } catch {
    // Ignore storage access errors
  }
}

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

function getMascotSeason(date: Date = new Date()): MascotSeason {
  const year = date.getFullYear();
  const month = date.getMonth();
  const day = date.getDate();

  if (month === 11 || (month === 0 && day <= 8)) {
    return 'christmas';
  }

  if ((month === 9 && day >= 24) || (month === 10 && day <= 2)) {
    return 'halloween';
  }

  const currentUtc = Date.UTC(year, month, day);
  const easterUtc = getEasterSundayUtc(year).getTime();
  const daysFromEaster = Math.round((currentUtc - easterUtc) / MS_PER_DAY);
  if (daysFromEaster >= -7 && daysFromEaster <= 2) {
    return 'easter';
  }

  if (month >= 5 && month <= 7) {
    return 'summer';
  }

  return 'default';
}

/**
 * Returns the seasonal DOrc deploy mascot illustration path for the given date:
 * - Christmas (`/modern-dorc-deploy-christmas.webp`): December 1 – January 8 (includes Orthodox Christmas on Jan 7)
 * - Halloween (`/modern-dorc-deploy-halloween.webp`): October 24 – November 2
 * - Easter (`/modern-dorc-deploy-easter.webp`): Palm Sunday through Easter Tuesday (Easter - 7 days to Easter + 2 days)
 * - Summer (`/modern-dorc-deploy-summer.webp`): Meteorological summer (June 1 – August 31)
 * - Default (`/modern-dorc-deploy.webp`): All other dates
 */
export function getDeployMascotIconPath(
  date: Date = new Date(),
  themeOverride?: MascotTheme
): string {
  const effectiveTheme = themeOverride ?? getDeployMascotThemeOverride();
  const season: MascotSeason =
    effectiveTheme === 'auto' ? getMascotSeason(date) : effectiveTheme;

  switch (season) {
    case 'christmas':
      return CHRISTMAS_DEPLOY_MASCOT_ICON;
    case 'halloween':
      return HALLOWEEN_DEPLOY_MASCOT_ICON;
    case 'easter':
      return EASTER_DEPLOY_MASCOT_ICON;
    case 'summer':
      return SUMMER_DEPLOY_MASCOT_ICON;
    default:
      return DEFAULT_DEPLOY_MASCOT_ICON;
  }
}
