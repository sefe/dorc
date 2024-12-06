import type { Route } from '@vaadin/router';

import AppConfig from '../app-config';

export const routes: Route[] = [
  {
    path: '',
    component: 'dorc-app',
    children: [
      {
        path: '/deploy',
        name: 'deploy',
        component: 'page-deploy',
        metadata: {
          title: new AppConfig().appName,
          titleTemplate: null,
          description: new AppConfig().appDescription
        },
        action: async () => {
          await import('../pages/page-deploy');
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
        action: async () => {
          await import('../pages/page-monitor-requests');
        }
      },
      {
        path: '/monitor-result/:id',
        name: 'monitor-result',
        component: 'page-monitor-result',
        metadata: {
          title: 'Monitor Result of Deployment',
          description: 'The details of a currently running job'
        },
        action: async () => {
          await import('../pages/page-monitor-result');
        }
      },
      {
        path: '/about',
        name: 'about',
        component: 'page-about',
        metadata: {
          title: 'About',
          description: 'About page description'
        },
        action: async () => {
          await import('../pages/page-about');
        }
      },
      {
        path: '/projects',
        name: 'projects',
        component: 'page-projects-list',
        metadata: {
          title: 'Projects',
          description: 'List of all projects you have permission to view'
        },
        action: async () => {
          await import('../pages/page-projects-list');
        }
      },
      {
        path: '/environments',
        name: 'environments',
        component: 'page-environments-list',
        metadata: {
          title: 'Environments',
          description: 'List of all environments you have permission to view'
        },
        action: async () => {
          await import('../pages/page-environments-list');
        }
      },
      {
        path: '/servers',
        name: 'servers',
        component: 'page-servers-list',
        metadata: {
          title: 'Servers',
          description: 'List of all servers you have permission to view'
        },
        action: async () => {
          await import('../pages/page-servers-list');
        }
      },
      {
        path: '/databases',
        name: 'databases',
        component: 'page-databases-list',
        metadata: {
          title: 'Databases',
          description: 'List of all databases you have permission to view'
        },
        action: async () => {
          await import('../pages/page-databases-list');
        }
      },
      {
        path: '/users',
        name: 'users',
        component: 'page-users-list',
        metadata: {
          title: 'Users',
          description: 'List of all environments you have permission to view'
        },
        action: async () => {
          await import('../pages/page-users-list');
        }
      },
      {
        path: '/daemons',
        name: 'daemons',
        component: 'page-daemons-list',
        metadata: {
          title: 'Daemons',
          description: 'List of all daemons'
        },
        action: async () => {
          await import('../pages/page-daemons-list');
        }
      },
      {
        path: '/scripts',
        name: 'scripts',
        component: 'page-scripts-list',
        metadata: {
          title: 'Scripts',
          description: 'List of all scripts'
        },
        action: async () => {
          await import('../pages/page-scripts-list');
        }
      },
      {
        path: '/variables',
        name: 'variables',
        component: 'page-variables',
        metadata: {
          title: 'Variables',
          description: 'List of all variables'
        },
        action: async () => {
          await import('../pages/page-variables');
        }
      },
      {
        path: '/variables/value-lookup',
        name: 'variables-value-lookup',
        component: 'page-variables-value-lookup',
        metadata: {
          title: 'Variables Values Lookup',
          description: 'List of all variables value for search'
        },
        action: async () => {
          await import('../pages/page-variables-value-lookup');
        }
      },
      {
        path: '/variables/audit',
        name: 'variables-audit',
        component: 'page-variables-audit',
        metadata: {
          title: 'Variables Values Audit',
          description: 'List of all variables value changes'
        },
        action: async () => {
          await import('../pages/page-variables-audit');
        }
      },
      {
        path: '/sql-roles',
        name: 'sql-roles',
        component: 'page-permissions-list',
        metadata: {
          title: 'SQL Roles',
          description: 'List of all Database Roles'
        },
        action: async () => {
          await import('../pages/page-permissions-list');
        }
      },
      {
        path: '/configuration',
        name: 'configuration',
        component: 'page-config-values-list',
        metadata: {
          title: 'Config Values',
          description: 'List of all configuration values'
        },
        action: async () => {
          await import('../pages/page-config-values-list');
        }
      },
      {
        path: '/sql-ports',
        name: 'sql-ports',
        component: 'page-sql-ports-list',
        metadata: {
          title: 'SQL Ports',
          description: 'List of all Database Ports'
        },
        action: async () => {
          await import('../pages/page-sql-ports-list');
        }
      },
      {
        path: '/env-history',
        name: 'env-history',
        component: 'page-env-history',
        metadata: {
          title: 'Environment History',
          description: 'The history of this environment'
        },
        action: async () => {
          await import('../pages/page-env-history');
        }
      },
      {
        path: '/project-envs/:id',
        name: 'project-envs',
        component: 'page-project-envs',
        metadata: {
          title: 'Environments for Project',
          description: 'All environments attached to a project'
        },
        action: async () => {
          await import('../pages/page-project-envs');
        }
      },
      {
        path: '/project-ref-data/:id',
        name: 'project-ref-data',
        component: 'project-ref-data',
        metadata: {
          title: 'Project Reference Data',
          description: 'The reference data for this environment'
        },
        action: async () => {
          await import('../pages/page-project-ref-data');
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
        action: async () => {
          await import('../pages/page-environment');
        },
        children: [
          {
            path: '/metadata',
            component: 'env-metadata',
            action: async () => {
              await import('../components/environment-tabs/env-metadata');
            }
          },
          {
            path: '/servers',
            component: 'env-servers',
            action: async () => {
              await import('../components/environment-tabs/env-servers');
            }
          },
          {
            path: '/databases',
            component: 'env-databases',
            action: async () => {
              await import('../components/environment-tabs/env-databases');
            }
          },
          {
            path: '/daemons',
            component: 'env-daemons',
            action: async () => {
              await import('../components/environment-tabs/env-daemons');
            }
          },
          {
            path: '/deployments',
            component: 'env-deployments',
            action: async () => {
              await import('../components/environment-tabs/env-deployments');
            }
          },
          {
            path: '/users',
            component: 'env-users',
            action: async () => {
              await import('../components/environment-tabs/env-users');
            }
          },
          {
            path: '/delegated-users',
            component: 'env-delegated-users',
            action: async () => {
              await import(
                '../components/environment-tabs/env-delegated-users'
              );
            }
          },
          {
            path: '/variables',
            component: 'env-variables',
            action: async () => {
              await import('../components/environment-tabs/env-variables');
            }
          },
          {
            path: '/projects',
            component: 'env-projects',
            action: async () => {
              await import('../components/environment-tabs/env-projects');
            }
          },
          {
            path: '/control-center',
            component: 'env-control-center',
            action: async () => {
              await import('../components/environment-tabs/env-control-center');
            }
          }
        ]
      },
      {
        path: '/',
        name: 'default',
        component: 'page-deploy',
        metadata: {
          title: new AppConfig().appName,
          titleTemplate: null,
          description: new AppConfig().appDescription
        },
        action: async () => {
          await import('../pages/page-deploy');
        }
      },
      {
        path: '(.*)',
        name: 'not-found',
        component: 'page-not-found',
        metadata: {
          title: 'Error',
          description: null,
          image: null
        },
        action: async () => {
          await import('../pages/page-not-found');
        }
      }
    ],
    action: async () => {
      await import('../components/dorc-app');
    }
  }
];
