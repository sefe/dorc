import type { Params } from '@vaadin/router';
import { Router } from '@vaadin/router';
import './style-registrations';
import { routes } from './routes';

export const router = new Router(document.querySelector('#outlet'));

router.setRoutes([
  // Redirect to URL without trailing slash
  {
    path: '(.*)/',
    action: (context, commands) => {
      const newPath = context.pathname.slice(0, -1);
      return commands.redirect(newPath);
    }
  },
  ...routes
]);

export const urlForName = (name: string, params?: Params) =>
  router.urlForName(name, params);
