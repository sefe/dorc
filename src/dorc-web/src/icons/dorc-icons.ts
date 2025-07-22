/**
 * DOrc Custom Icon Set
 * Consistent, modern icons for the DOrc application
 * 
 * All icons follow a consistent design system:
 * - 24x24px base size
 * - 2px stroke width
 * - 2px border radius where applicable
 * - Consistent visual weight and optical balance
 */
import '@vaadin/icon/vaadin-iconset.js';

const template = document.createElement('template');

template.innerHTML = `<vaadin-iconset name="dorc" size="24">
<svg><defs>

<!-- Action Icons -->
<g id="edit">
  <path d="M3 17.25V21h3.75L17.81 9.94l-3.75-3.75L3 17.25zM20.71 7.04c.39-.39.39-1.02 0-1.41l-2.34-2.34c-.39-.39-1.02-.39-1.41 0l-1.83 1.83 3.75 3.75 1.83-1.83z" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
</g>

<g id="delete">
  <path d="M6 7v12c0 1.1.9 2 2 2h8c1.1 0 2-.9 2-2V7H6z" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M4 7h16M10 11v6M14 11v6M8 7V5c0-.6.4-1 1-1h6c.6 0 1 .4 1 1v2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="save">
  <path d="M19 21H5c-1.1 0-2-.9-2-2V5c0-1.1.9-2 2-2h11l5 5v11c0 1.1-.9 2-2 2z" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linejoin="round"/>
  <path d="M7 3v6h8" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
  <circle cx="12" cy="15" r="2" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
</g>

<g id="refresh">
  <path d="M21.5 2v6h-6M2.5 22v-6h6M21 12c-.5 5.2-4.8 9-10 9-2.8 0-5.3-1.1-7.1-2.9M3 12c.5-5.2 4.8-9 10-9 2.8 0 5.3 1.1 7.1 2.9" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
</g>

<g id="copy">
  <rect x="9" y="9" width="13" height="13" rx="2" ry="2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="clear">
  <circle cx="12" cy="12" r="10" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M15 9l-6 6M9 9l6 6" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="close">
  <path d="M18 6L6 18M6 6l12 12" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<!-- Media/Control Icons -->
<g id="play">
  <polygon points="5,3 19,12 5,21" 
           fill="none" 
           stroke="currentColor" 
           stroke-width="2" 
           stroke-linejoin="round"/>
</g>

<g id="stop">
  <rect x="6" y="6" width="12" height="12" rx="2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="repeat">
  <path d="M17 1l4 4-4 4M3 11V9a4 4 0 0 1 4-4h14M7 23l-4-4 4-4M21 13v2a4 4 0 0 1-4 4H3" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
</g>

<!-- Security Icons -->
<g id="lock">
  <rect x="3" y="11" width="18" height="11" rx="2" ry="2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M7 11V7a5 5 0 0 1 10 0v4" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="unlock">
  <rect x="3" y="11" width="18" height="11" rx="2" ry="2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M7 11V7a5 5 0 0 1 9.9-1" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="key">
  <path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
</g>

<g id="safe">
  <rect x="3" y="5" width="18" height="14" rx="2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <circle cx="12" cy="12" r="3" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M7 5V3a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<!-- Connectivity Icons -->
<g id="link">
  <path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
</g>

<g id="unlink">
  <path d="M18.84 12.25l1.72-1.71a5 5 0 0 0-7.07-7.07l-1.71 1.71M8.91 14.56l1.71 1.71a5 5 0 0 0 7.07-7.07l-1.71-1.71M14 11a5 5 0 0 0-5 0M8 13l2 2M10 11l2 2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
  <path d="M2 2l20 20" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="connect">
  <circle cx="12" cy="12" r="3" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M12 1v6M12 17v6M4.22 4.22l4.24 4.24M15.54 15.54l4.24 4.24M1 12h6M17 12h6M4.22 19.78l4.24-4.24M15.54 8.46l4.24-4.24" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<!-- Infrastructure Icons -->
<g id="server">
  <rect x="2" y="3" width="20" height="4" rx="1" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <rect x="2" y="9" width="20" height="4" rx="1" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <rect x="2" y="15" width="20" height="4" rx="1" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M6 5h0M6 11h0M6 17h0" 
        stroke="currentColor" 
        stroke-width="3" 
        stroke-linecap="round"/>
</g>

<g id="database">
  <ellipse cx="12" cy="5" rx="9" ry="3" 
           fill="none" 
           stroke="currentColor" 
           stroke-width="2"/>
  <path d="M3 5v14c0 1.66 4.03 3 9 3s9-1.34 9-3V5" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M3 12c0 1.66 4.03 3 9 3s9-1.34 9-3" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="environment">
  <rect x="4" y="4" width="16" height="16" rx="2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M9 9h6v6H9zM4 9h2M18 9h2M9 4v2M9 18v2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="desktop">
  <rect x="2" y="3" width="20" height="14" rx="2" ry="2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M8 21h8M12 17v4" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="container">
  <path d="M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16z" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <polyline points="3.27,6.96 12,12.01 20.73,6.96" 
            fill="none" 
            stroke="currentColor" 
            stroke-width="2" 
            stroke-linejoin="round"/>
  <line x1="12" y1="22.08" x2="12" y2="12" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<!-- User Management Icons -->
<g id="user">
  <path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <circle cx="12" cy="7" r="4" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
</g>

<g id="users">
  <path d="M16 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <circle cx="8.5" cy="7" r="4" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="group">
  <circle cx="9" cy="7" r="4" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M1 21v-2a4 4 0 0 1 4-4h8a4 4 0 0 1 4 4v2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <circle cx="19" cy="7" r="2" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M23 21v-2a4 4 0 0 0-2-3.5" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="group-add">
  <circle cx="9" cy="7" r="4" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M1 21v-2a4 4 0 0 1 4-4h8a4 4 0 0 1 4 4v2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M20 8v6M23 11h-6" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<!-- Configuration Icons -->
<g id="settings">
  <circle cx="12" cy="12" r="3" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="automation">
  <path d="M8 2v4M16 2v4M3 10h18M8 14h8M10 18h4" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
  <rect x="3" y="4" width="18" height="16" rx="2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="code">
  <path d="M16 18l6-6-6-6M8 6l-6 6 6 6" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
</g>

<!-- Navigation Icons -->
<g id="menu">
  <line x1="3" y1="6" x2="21" y2="6" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
  <line x1="3" y1="12" x2="21" y2="12" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
  <line x1="3" y1="18" x2="21" y2="18" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="expand">
  <path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
</g>

<g id="more">
  <circle cx="12" cy="12" r="1" 
          fill="currentColor"/>
  <circle cx="19" cy="12" r="1" 
          fill="currentColor"/>
  <circle cx="5" cy="12" r="1" 
          fill="currentColor"/>
</g>

<!-- Status/Info Icons -->
<g id="info">
  <circle cx="12" cy="12" r="10" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M12 16v-4M12 8h.01" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="error">
  <circle cx="12" cy="12" r="10" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M15 9l-6 6M9 9l6 6" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="history">
  <circle cx="12" cy="12" r="10" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M12 6v6l4 2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<!-- Data Management Icons -->
<g id="archive">
  <rect x="2" y="3" width="20" height="5" rx="1" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8M10 12h4" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="package">
  <path d="M16.5 9.4L7.55 4.24M7.45 4.21l-.05-.03M7.42 4.18C7.28 4.1 7.15 4.05 7 4.05c-.17 0-.34.09-.47.24l-.02.02M16.5 9.4c.21-.13.5-.13.71 0l6.27 4.19c.5.33.52 1.02.05 1.37l-8.09 6.04c-.46.34-1.1.34-1.56 0L1.27 14.96c-.47-.35-.45-1.04.05-1.37L7.59 9.4" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M12 22.08V12" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="list">
  <line x1="8" y1="6" x2="21" y2="6" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
  <line x1="8" y1="12" x2="21" y2="12" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
  <line x1="8" y1="18" x2="21" y2="18" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
  <line x1="3" y1="6" x2="3.01" y2="6" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
  <line x1="3" y1="12" x2="3.01" y2="12" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
  <line x1="3" y1="18" x2="3.01" y2="18" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="search">
  <circle cx="11" cy="11" r="8" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M21 21l-4.35-4.35" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<!-- Communication Icons -->
<g id="notification">
  <circle cx="12" cy="12" r="4" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M16 8v5a3 3 0 0 0 6 0v-5a10 10 0 1 0-20 0v5a3 3 0 0 0 6 0V8a4 4 0 1 1 8 0z" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<g id="clipboard">
  <path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <rect x="8" y="2" width="8" height="4" rx="1" ry="1" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
</g>

<!-- Custom DOrc Icons -->
<g id="powershell">
  <rect x="2" y="4" width="20" height="16" rx="2" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2"/>
  <path d="M6 8l4 4-4 4M12 16h6" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
</g>

<g id="variables">
  <path d="M7 20l4-16m2 16l4-16M6 9h14M4 15h14" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="admin">
  <path d="M9 12l2 2 4-4m6 2a9 9 0 1 1-18 0 9 9 0 0 1 18 0z" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
  <path d="M12 3v2M12 19v2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M3 12h2M19 12h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="1" 
        stroke-linecap="round"/>
</g>

<!-- Additional Missing Icons -->
<g id="close-small">
  <path d="M14 10l-4 4M10 10l4 4" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

<g id="child">
  <circle cx="9" cy="9" r="2" 
          fill="none" 
          stroke="currentColor" 
          stroke-width="2"/>
  <path d="M21 15v6h-6M3 9h6v6" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round" 
        stroke-linejoin="round"/>
  <path d="M15 9h6M9 21v-6" 
        fill="none" 
        stroke="currentColor" 
        stroke-width="2" 
        stroke-linecap="round"/>
</g>

</defs></svg>
</vaadin-iconset>`;

document.head.appendChild(template.content);