import type { Params } from '@vaadin/router';
import { Router } from '@vaadin/router';
import './style-registrations';
import { RouteMeta } from './routes.ts';

export const router = new Router<RouteMeta>(document.querySelector('#outlet'));

export const urlForName = (name: string, params?: Params) =>
  router.urlForName(name, params);
