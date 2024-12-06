import { removeMetaTag, setMetaTag } from '../helpers/html-meta-manager/utils';

import { PageElement } from '../helpers/page-element';

export class PageElementNotFound extends PageElement {
  connectedCallback() {
    super.connectedCallback();

    setMetaTag('name', 'render:status_code', '404');
  }

  disconnectedCallback() {
    removeMetaTag('name', 'render:status_code');

    super.disconnectedCallback();
  }
}
