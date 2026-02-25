import { expect, fixture, html } from '@open-wc/testing';
import './project-card.js';
import type { ProjectCard } from './project-card.js';

describe('ProjectCard responsive layout', () => {
  const mockProject = {
    ProjectId: 1,
    ProjectName: 'Test Project',
    ProjectDescription: 'A test project description',
    ArtefactsUrl: '',
    ArtefactsSubPaths: '',
    ArtefactsBuildRegex: '',
  };

  it('should use flex layout instead of float', async () => {
    const el = await fixture<ProjectCard>(html`
      <project-card .project="${mockProject}"></project-card>
    `);
    await el.updateComplete;

    const cardElement = el.shadowRoot!.querySelector('.card-element') as HTMLElement;
    const computedStyle = getComputedStyle(cardElement);

    expect(computedStyle.display).to.equal('flex');
    expect(computedStyle.alignItems).to.equal('center');
    expect(computedStyle.justifyContent).to.equal('space-between');
  });

  it('should use box-sizing border-box', async () => {
    const el = await fixture<ProjectCard>(html`
      <project-card .project="${mockProject}"></project-card>
    `);
    await el.updateComplete;

    const cardElement = el.shadowRoot!.querySelector('.card-element') as HTMLElement;
    const computedStyle = getComputedStyle(cardElement);

    expect(computedStyle.boxSizing).to.equal('border-box');
  });

  it('should NOT use float layout', async () => {
    const el = await fixture<ProjectCard>(html`
      <project-card .project="${mockProject}"></project-card>
    `);
    await el.updateComplete;

    const allDivs = el.shadowRoot!.querySelectorAll('div');
    allDivs.forEach(div => {
      const style = getComputedStyle(div);
      expect(style.float).to.be.oneOf(['none', ''],
        `Found float: ${style.float} on element with class="${div.className}"`);
    });
  });

  it('should have card-content with flex: 1', async () => {
    const el = await fixture<ProjectCard>(html`
      <project-card .project="${mockProject}"></project-card>
    `);
    await el.updateComplete;

    const cardContent = el.shadowRoot!.querySelector('.card-content') as HTMLElement;
    expect(cardContent).to.not.be.null;

    const computedStyle = getComputedStyle(cardContent);
    expect(computedStyle.flexGrow).to.equal('1');
  });

  it('should render project name', async () => {
    const el = await fixture<ProjectCard>(html`
      <project-card .project="${mockProject}"></project-card>
    `);
    await el.updateComplete;

    const heading = el.shadowRoot!.querySelector('h3');
    expect(heading).to.not.be.null;
    expect(heading!.textContent!.trim()).to.equal('Test Project');
  });

  it('should render project description when provided', async () => {
    const el = await fixture<ProjectCard>(html`
      <project-card .project="${mockProject}"></project-card>
    `);
    await el.updateComplete;

    const text = el.shadowRoot!.querySelector('.card-element__text');
    expect(text).to.not.be.null;
    expect(text!.textContent!.trim()).to.equal('A test project description');
  });

  it('should render "No Description" when description is empty', async () => {
    const emptyProject = { ...mockProject, ProjectDescription: '' };
    const el = await fixture<ProjectCard>(html`
      <project-card .project="${emptyProject}"></project-card>
    `);
    await el.updateComplete;

    const text = el.shadowRoot!.querySelector('.card-element__text');
    expect(text).to.not.be.null;
    expect(text!.textContent!.trim()).to.equal('No Description');
  });

  it('should render action button', async () => {
    const el = await fixture<ProjectCard>(html`
      <project-card .project="${mockProject}"></project-card>
    `);
    await el.updateComplete;

    const buttons = el.shadowRoot!.querySelectorAll('vaadin-button');
    expect(buttons.length).to.equal(1);
  });
});
