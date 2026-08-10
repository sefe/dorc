import { expect } from '../_helpers';
import { MAX_TAG_STRING_LENGTH } from '../../src/helpers/tag-limits';
import swagger from '../../src/apis/dorc-api/swagger.json';

// Cross-language layer-agreement check:
// the UI constant and the API contract's maxLength must be the same number. The C#
// side's agreement with the spec is inherent — the spec is generated from the
// [StringLength] attributes.
describe('tag capacity layer agreement', () => {
  const schemas = (swagger as any).components.schemas;

  it('server Tags maxLength matches the UI constant', () => {
    expect(schemas.ServerApiModel.properties.Tags.maxLength).to.equal(
      MAX_TAG_STRING_LENGTH
    );
  });

  it('database Tags maxLength matches the UI constant', () => {
    // The database tag column is deploy.Database.Tags.
    expect(schemas.DatabaseApiModel.properties.Tags.maxLength).to.equal(
      MAX_TAG_STRING_LENGTH
    );
  });
});
