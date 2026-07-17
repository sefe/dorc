import { expect } from '../_helpers';
import { MAX_TAG_STRING_LENGTH } from '../../src/helpers/tag-limits';
import swagger from '../../src/apis/dorc-api/swagger.json';

// SC-2's cross-language layer-agreement check (docs/tag-capacity-expansion, IS S-003):
// the UI constant and the API contract's maxLength must be the same number. The C#
// side's agreement with the spec is inherent — the spec is generated from the
// [StringLength] attributes.
describe('tag capacity layer agreement', () => {
  const schemas = (swagger as any).components.schemas;

  it('server ApplicationTags maxLength matches the UI constant', () => {
    expect(schemas.ServerApiModel.properties.ApplicationTags.maxLength).to.equal(
      MAX_TAG_STRING_LENGTH
    );
  });
});
