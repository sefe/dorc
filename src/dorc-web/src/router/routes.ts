import type { Route } from '@vaadin/router';
import {appConfig} from '../app-config';

import '../components/dorc-app.ts'
import '../components/environment-tabs/env-control-center.ts'
import '../components/environment-tabs/env-daemons.ts'
import '../components/environment-tabs/env-databases.ts'
import '../components/environment-tabs/env-delegated-users.ts'
import '../components/environment-tabs/env-deployments.ts'
import '../components/environment-tabs/env-metadata.ts'
import '../components/environment-tabs/env-projects.ts'
import '../components/environment-tabs/env-servers.ts'
import '../components/environment-tabs/env-users.ts'
import '../components/environment-tabs/env-variables.ts'
import '../components/environment-tabs/env-tenants.ts'
import '../pages/page-about.ts'
import '../pages/page-config-values-list.ts'
import '../pages/page-daemons-list.ts'
import '../pages/page-databases-list.ts'
import '../pages/page-deploy.ts'
import '../pages/page-env-history.ts'
import '../pages/page-environment.ts'
import '../pages/page-environments-list.ts'
import '../pages/page-monitor-requests.ts'
import '../pages/page-monitor-result.ts'
import '../pages/page-not-found.ts'
import '../pages/page-permissions-list.ts'
import '../pages/page-project-envs.ts'
import '../pages/page-project-bundles.ts'
import '../pages/page-project-ref-data.ts'
import '../pages/page-projects-list.ts'
import '../pages/page-scripts-list.ts'
import '../pages/page-scripts-audit.ts'
import '../pages/page-servers-list.ts'
import '../pages/page-sql-ports-list.ts'
import '../pages/page-users-list.ts'
import '../pages/page-variables-audit.ts'
import '../pages/page-variables-value-lookup.ts'
import '../pages/page-variables.ts'

export type RouteMeta = Readonly<{
  metadata: {
    title: string;
    description: string;
  };
}>;

export const routes: Route<RouteMeta>[] = [
  {
    path: '',
    component: 'dorc-app',
    metadata: {
      title: appConfig.appName,
      description: appConfig.appDescription
    },
    children: [
      {
        path: '/deploy',
        name: 'deploy',
        component: 'page-deploy',
        metadata: {
          title: appConfig.appName,
          description: appConfig.appDescription
        }
      },
      {
        path: '/monitor-requests',
        name: 'monitor-requests',
        component: 'page-monitor-requests',
        metadata: {
          title: 'Monitor Requests',
          description: 'List of all currently running requests'
        },
      },
      {
        path: '/monitor-result/:id',
        name: 'monitor-result',
        component: 'page-monitor-result',
        metadata: {
          title: 'Monitor Result of Deployment',
          description: 'The details of a currently running job'
        }
      },
      {
        path: '/about',
        name: 'about',
        component: 'page-about',
        metadata: {
          title: 'About',
          description: 'About page description'
        }
      },
      {
        path: '/projects',
        name: 'projects',
        component: 'page-projects-list',
        metadata: {
          title: 'Projects',
          description: 'List of all projects you have permission to view'
        }
      },
      {
        path: '/environments',
        name: 'environments',
        component: 'page-environments-list',
        metadata: {
          title: 'Environments',
          description: 'List of all environments you have permission to view'
        }
      },
      {
        path: '/servers',
        name: 'servers',
        component: 'page-servers-list',
        metadata: {
          title: 'Servers',
          description: 'List of all servers you have permission to view'
        }
      },
      {
        path: '/databases',
        name: 'databases',
        component: 'page-databases-list',
        metadata: {
          title: 'Databases',
          description: 'List of all databases you have permission to view'
        }
      },
      {
        path: '/users',
        name: 'users',
        component: 'page-users-list',
        metadata: {
          title: 'Users',
          description: 'List of all environments you have permission to view'
        }
      },
      {
        path: '/daemons',
        name: 'daemons',
        component: 'page-daemons-list',
        metadata: {
          title: 'Daemons',
          description: 'List of all daemons'
        }
      },
      {
        path: '/scripts',
        name: 'scripts',
        component: 'page-scripts-list',
        metadata: {
          title: 'Scripts',
          description: 'List of all scripts'
        }
      },
      {
        path: '/scripts/audit',
        name: 'scripts-audit',
        component: 'page-scripts-audit',
        metadata: {
          title: 'Scripts Values Audit',
          description: 'List of all scripts value changes'
        }
      },
      {
        path: '/variables',
        name: 'variables',
        component: 'page-variables',
        metadata: {
          title: 'Variables',
          description: 'List of all variables'
        }
      },
      {
        path: '/variables/value-lookup',
        name: 'variables-value-lookup',
        component: 'page-variables-value-lookup',
        metadata: {
          title: 'Variables Values Lookup',
          description: 'List of all variables value for search'
        }
      },
      {
        path: '/variables/audit',
        name: 'variables-audit',
        component: 'page-variables-audit',
        metadata: {
          title: 'Variables Values Audit',
          description: 'List of all variables value changes'
        }
      },
      {
        path: '/sql-roles',
        name: 'sql-roles',
        component: 'page-permissions-list',
        metadata: {
          title: 'SQL Roles',
          description: 'List of all Database Roles'
        }
      },
      {
        path: '/configuration',
        name: 'configuration',
        component: 'page-config-values-list',
        metadata: {
          title: 'Config Values',
          description: 'List of all configuration values'
        }
      },
      {
        path: '/sql-ports',
        name: 'sql-ports',
        component: 'page-sql-ports-list',
        metadata: {
          title: 'SQL Ports',
          description: 'List of all Database Ports'
        }
      },
      {
        path: '/env-history',
        name: 'env-history',
        component: 'page-env-history',
        metadata: {
          title: 'Environment History',
          description: 'The history of this environment'
        }
      },
      {
        path: '/project-envs/:id',
        name: 'project-envs',
        component: 'page-project-envs',
        metadata: {
          title: 'Environments for Project',
          description: 'All environments attached to a project'
        }
      },
      {
        path: '/project-envs/:id/bundles',
        name: 'project-bundles',
        component: 'page-project-bundles',
        metadata: {
          title: 'Bundles for Project',
          description: 'All bundles for a project'
        }
      },
      {
        path: '/project-ref-data/:id',
        name: 'project-ref-data',
        component: 'page-project-ref-data',
        metadata: {
          title: 'Project Reference Data',
          description: 'The reference data for this environment'
        }
      },
      {
        path: '/environment/:id',
        name: 'environment',
        component: 'page-environment',
        metadata: {
          title: 'Environment',
          description: 'The details of this environment'
        },
        children: [
          {
            path: '/metadata',
            component: 'env-metadata',
            metadata: {
              title: 'Metadata',
              description: 'Environment metadata details'
            }
          },
          {
            path: '/servers',
            component: 'env-servers',
            metadata: {
              title: 'Servers',
              description: 'Environment servers details'
            }
          },
          {
            path: '/databases',
            component: 'env-databases',
            metadata: {
              title: 'Databases',
              description: 'Environment database details'
            }
          },
          {
            path: '/daemons',
            component: 'env-daemons',
            metadata: {
              title: 'Daemons',
              description: 'Environment daemons details'
            }
          },
          {
            path: '/deployments',
            component: 'env-deployments',
            metadata: {
              title: 'Deployments',
              description: 'Environment deployment details'
            }
          },
          {
            path: '/users',
            component: 'env-users',
            metadata: {
              title: 'Users',
              description: 'Environment user details'
            }
          },
          {
            path: '/delegated-users',
            component: 'env-delegated-users',
            metadata: {
              title: 'Delegated Users',
              description: 'Environment delegated User details'
            }
          },
          {
            path: '/variables',
            component: 'env-variables',
            metadata: {
              title: 'variables',
              description: 'Environment variables details'
            }
          },
          {
            path: '/projects',
            component: 'env-projects',
            metadata: {
              title: 'Projects',
              description: 'Environment projects details'
            }
          },
          {
            path: '/control-center',
            component: 'env-control-center',
            metadata: {
              title: 'Control Center',
              description: 'Environment control center'
            }
          },
          {
            path: '/tenants',
            component: 'env-tenants',
            metadata: {
              title: 'Tenants',
              description: 'Environment tenants details'
            }
          }
        ]
      },
      {
        path: '/',
        name: 'default',
        component: 'page-deploy',
        metadata: {
          title: appConfig.appName,
          description: appConfig.appDescription
        }
      },
      {
        path: '(.*)',
        name: 'not-found',
        component: 'page-not-found',
        metadata: {
          title: 'Error',
          description: 'Page not found',
        }
      }
    ],
  }
];
