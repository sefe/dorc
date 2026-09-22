import { expect } from 'vitest';
import { MAX_TAG_LENGTH } from '../../src/helpers/tag-limits';
import swagger from '../../src/apis/dorc-api/swagger.json';

describe('tag limit layer agreement', () => {
  const schemas = (swagger as any).components.schemas;

  // The API and the database agree that the limit is per tag: deploy.DatabaseTag.Tag
  // and deploy.ServerTag.Tag are NVARCHAR(200), [TagSet] enforces it server-side, and
  // the UI rejects an over-long chip before submitting. This pins the UI constant to
  // the committed contract so the three cannot drift apart silently.
  it('server tag item maxLength matches the UI constant', () => {
    expect(schemas.ServerApiModel.properties.Tags.items.maxLength).to.equal(
      MAX_TAG_LENGTH
    );
  });

  it('database tag item maxLength matches the UI constant', () => {
    expect(schemas.DatabaseApiModel.properties.Tags.items.maxLength).to.equal(
      MAX_TAG_LENGTH
    );
  });

  it('tags are carried as arrays, not a delimited string', () => {
    expect(schemas.DatabaseApiModel.properties.Tags.type).to.equal('array');
    expect(schemas.ServerApiModel.properties.Tags.type).to.equal('array');
  });
});
