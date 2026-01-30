import { customElement, property } from 'lit/decorators.js';
import { LitElement, PropertyValues } from 'lit';
import {
  DeploymentRequestApiModel,
  EnvironmentApiModel,
  ProjectApiModel
} from '../apis/dorc-api';
import { Router } from '@vaadin/router';
import { setCookie } from '../helpers/cookies.ts';
import { DorcNavbar } from './dorc-navbar.ts';
import { EnvPageTabNames } from '../pages/page-environment.ts';

@customElement('shortcuts-store')
export class ShortcutsStore extends LitElement {
  private monitorResultTabs = 'monitor-result-tabs';
  private envDetailTabs = 'env-detail-tabs';
  private projectEnvsTabs = 'project-envs-tabs';

  @property() metaData = '';
  protected dorcNavbar: DorcNavbar | undefined;
  protected dorcHelperPage: string | undefined;

  protected firstUpdated(_changedProperties: PropertyValues) {
    super.firstUpdated(_changedProperties);

    this.addEventListener(
      'open-env-detail',
      this.openEnvDetail as EventListener
    );
    this.addEventListener(
      'open-monitor-result',
      this.openMonitorResult as EventListener
    );
    this.addEventListener(
      'open-project-envs',
      this.openProjectEnvs as EventListener
    );
    this.addEventListener(
      'open-project-ref-data',
      this.openProjectRefData as EventListener
    );
    this.addEventListener(
      'environment-deleted',
      this.environmentDeleted as EventListener
    );
  }

  environmentDeleted(e: CustomEvent) {
    this.dorcNavbar?.closeEnvDetail(e);

    const path = '/projects';
    Router.go(path);

    this.dorcNavbar?.setSelectedTab(path);
  }

  updated() {
    this.dorcNavbar?.setSelectedTab(window.location.pathname);
  }

  private openEnvDetail(e: CustomEvent) {
    const env = e.detail.Environment as EnvironmentApiModel;
    const tab = e.detail.Tab as EnvPageTabNames;
    
    const existingEnvs = this.dorcNavbar?.openEnvTabs.find(
      value => value.EnvironmentName === env.EnvironmentName
    );
    let path = '';
    if (existingEnvs === undefined) {
      this.dorcNavbar?.openEnvTabs.push(env);
      this.dorcNavbar?.insertEnvTab(env);
      console.log('inserted new tab');
    }

    path = this.getEnvDetailPath(env, tab);

    Router.go(path);

    this.dorcNavbar?.setSelectedTab(this.getEnvDetailPath(env));

    setCookie(this.envDetailTabs, JSON.stringify(this.dorcNavbar?.openEnvTabs));
  }

  private openMonitorResult(e: CustomEvent) {
    const requestOrig = e.detail.request as DeploymentRequestApiModel;
    const request: DeploymentRequestApiModel = {
      Id: requestOrig.Id,
      EnvironmentName: requestOrig.EnvironmentName,
      BuildNumber: requestOrig.BuildNumber
    };

    const existingResults = this.dorcNavbar?.openResultTabs.find(
      value => value.Id === request.Id
    );
    let path = '';
    if (existingResults === undefined) {
      this.dorcNavbar?.openResultTabs.push(request);
      path = this.dorcNavbar?.insertResultTab(request) ?? '';
    } else {
      path = this.getMonitorResultPath(request);
    }

    this.dorcNavbar?.setSelectedTab(path);

    setCookie(this.monitorResultTabs, JSON.stringify(this.dorcNavbar?.openResultTabs));

    console.log(path);
    window.open(path);
  }

  private openProjectRefData(e: CustomEvent) {
    const project = e.detail.Project as ProjectApiModel;

    const path = `/project-ref-data/${project?.ProjectId}`;

    Router.go(path);
  }

  private openProjectEnvs(e: CustomEvent) {
    const project = e.detail.Project as ProjectApiModel;
    const existingProjs = this.dorcNavbar?.openProjTabs.find(
      value => value.ProjectName === project.ProjectName
    );
    let path = '';
    if (existingProjs === undefined) {
      project.ArtefactsSubPaths = ''; // This field can occasionally contain ';' which breaks the cookies
      this.dorcNavbar?.openProjTabs.push(project);
      path = this.dorcNavbar?.insertProjTab(project) ?? '';
    } else {
      path = this.getProjectEnvsPath(project);
    }

    Router.go(path);

    this.dorcNavbar?.setSelectedTab(path);

    setCookie(this.projectEnvsTabs, JSON.stringify(this.dorcNavbar?.openProjTabs));
  }

  private getProjectEnvsPath(projectAPIModel: ProjectApiModel) {
    return `/project-envs/${String(projectAPIModel.ProjectName)}`;
  }

  private getMonitorResultPath(result: DeploymentRequestApiModel) {
    return `/monitor-result/${String(result.Id)}`;
  }

  private getEnvDetailPath(env: EnvironmentApiModel, tab: EnvPageTabNames = EnvPageTabNames.Metadata) {
    return `/environment/${String(env.EnvironmentName)}/${tab}`;
  }
}
