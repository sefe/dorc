import { expect } from '../_helpers';
import { splitTags, joinTags, hasTag } from '../../src/helpers/tag-parser';

// docs/database-tags IS S-005: hasTag mirrors the backend TagString.HasTag
// contract — exact per-entry match after trimming; null/empty/whitespace on
// either side never matches.
describe('tag-parser hasTag', () => {
  it('matches an exact entry of a semicolon-separated list', () => {
    expect(hasTag('Endur;Reporting', 'Endur')).to.equal(true);
    expect(hasTag('Endur;Reporting', 'Reporting')).to.equal(true);
  });

  it('never matches substrings of entries', () => {
    expect(hasTag('Endur;Reporting', 'Endu')).to.equal(false);
    expect(hasTag('EndurX', 'Endur')).to.equal(false);
  });

  it('trims entries and the sought tag', () => {
    expect(hasTag(' Endur ; Ops ', 'Endur')).to.equal(true);
    expect(hasTag('Endur', ' Endur ')).to.equal(true);
  });

  it('is case-sensitive, matching the backend Ordinal semantics', () => {
    expect(hasTag('Endur', 'endur')).to.equal(false);
  });

  it('never matches on null/empty/whitespace input', () => {
    expect(hasTag(null, 'Endur')).to.equal(false);
    expect(hasTag('', 'Endur')).to.equal(false);
    expect(hasTag('Endur', '')).to.equal(false);
    expect(hasTag('Endur', '   ')).to.equal(false);
    expect(hasTag('Endur', null)).to.equal(false);
  });

  it('round-trips with splitTags/joinTags', () => {
    const joined = joinTags(splitTags('b;a'));
    expect(hasTag(joined, 'a')).to.equal(true);
    expect(hasTag(joined, 'b')).to.equal(true);
  });
});
