import { describe, expect, it } from 'vitest';
import {
  getDeployMascotIconPath,
  getDeployMascotThemeOverride,
  getEasterSundayUtc,
  setDeployMascotThemeOverride
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

  it('returns the Christmas deploy mascot from December 1 through Orthodox Christmas (January 8)', () => {
    expect(getDeployMascotIconPath(new Date(2026, 11, 1))).toBe(
      '/modern-dorc-deploy-christmas.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 11, 25))).toBe(
      '/modern-dorc-deploy-christmas.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 11, 31))).toBe(
      '/modern-dorc-deploy-christmas.webp'
    );
    expect(getDeployMascotIconPath(new Date(2027, 0, 1))).toBe(
      '/modern-dorc-deploy-christmas.webp'
    );
    expect(getDeployMascotIconPath(new Date(2027, 0, 7))).toBe(
      '/modern-dorc-deploy-christmas.webp'
    );
    expect(getDeployMascotIconPath(new Date(2027, 0, 8))).toBe(
      '/modern-dorc-deploy-christmas.webp'
    );
    expect(getDeployMascotIconPath(new Date(2027, 0, 9))).toBe(
      '/modern-dorc-deploy.webp'
    );
  });

  it('returns the Easter deploy mascot during Easter week (Palm Sunday through Easter Tuesday)', () => {
    // In 2026, Easter Sunday is April 5 (month index 3, day 5)
    expect(getDeployMascotIconPath(new Date(2026, 2, 28))).toBe(
      '/modern-dorc-deploy.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 2, 29))).toBe(
      '/modern-dorc-deploy-easter.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 3, 3))).toBe(
      '/modern-dorc-deploy-easter.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 3, 5))).toBe(
      '/modern-dorc-deploy-easter.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 3, 6))).toBe(
      '/modern-dorc-deploy-easter.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 3, 7))).toBe(
      '/modern-dorc-deploy-easter.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 3, 8))).toBe(
      '/modern-dorc-deploy.webp'
    );
  });

  it('returns the Summer deploy mascot during June, July, and August', () => {
    expect(getDeployMascotIconPath(new Date(2026, 4, 31))).toBe(
      '/modern-dorc-deploy.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 5, 1))).toBe(
      '/modern-dorc-deploy-summer.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 6, 15))).toBe(
      '/modern-dorc-deploy-summer.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 7, 31))).toBe(
      '/modern-dorc-deploy-summer.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 8, 1))).toBe(
      '/modern-dorc-deploy.webp'
    );
  });

  it('returns the Halloween deploy mascot from October 24 through November 2', () => {
    expect(getDeployMascotIconPath(new Date(2026, 9, 23))).toBe(
      '/modern-dorc-deploy.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 9, 24))).toBe(
      '/modern-dorc-deploy-halloween.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 9, 31))).toBe(
      '/modern-dorc-deploy-halloween.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 10, 2))).toBe(
      '/modern-dorc-deploy-halloween.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 10, 3))).toBe(
      '/modern-dorc-deploy.webp'
    );
  });

  it('returns the default deploy mascot outside holiday windows', () => {
    expect(getDeployMascotIconPath(new Date(2026, 0, 15))).toBe(
      '/modern-dorc-deploy.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 9, 20))).toBe(
      '/modern-dorc-deploy.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 10, 30))).toBe(
      '/modern-dorc-deploy.webp'
    );
  });

  it('allows overriding the deploy mascot theme explicitly or via storage', () => {
    expect(getDeployMascotIconPath(new Date(2026, 0, 15), 'halloween')).toBe(
      '/modern-dorc-deploy-halloween.webp'
    );
    expect(getDeployMascotIconPath(new Date(2026, 11, 25), 'default')).toBe(
      '/modern-dorc-deploy.webp'
    );

    setDeployMascotThemeOverride('christmas');
    expect(getDeployMascotThemeOverride()).toBe('christmas');
    expect(getDeployMascotIconPath(new Date(2026, 0, 15))).toBe(
      '/modern-dorc-deploy-christmas.webp'
    );

    setDeployMascotThemeOverride('auto');
    expect(getDeployMascotThemeOverride()).toBe('auto');
  });
});
