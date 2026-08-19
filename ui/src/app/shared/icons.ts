/** One shared icon set (inline SVG paths) for every document/photo/map-point action row across the app - Job Documents, Land Documents, Survey/Deed attachments, photo grid, map points. Keep new actions here instead of inlining another <svg> per component. */

export const ICONS = {
  view: `<path stroke-linecap="round" stroke-linejoin="round" d="M2.036 12.322a1.012 1.012 0 010-.639C3.423 7.51 7.36 4.5 12 4.5c4.638 0 8.573 3.007 9.963 7.178.07.207.07.431 0 .639C20.577 16.49 16.64 19.5 12 19.5c-4.638 0-8.573-3.007-9.963-7.178z" /><path stroke-linecap="round" stroke-linejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />`,
  download: `<path stroke-linecap="round" stroke-linejoin="round" d="M4 16v2a2 2 0 002 2h12a2 2 0 002-2v-2M7 10l5 5 5-5M12 15V3" />`,
  rename: `<path stroke-linecap="round" stroke-linejoin="round" d="M11 4H4a2 2 0 00-2 2v14a2 2 0 002 2h14a2 2 0 002-2v-7" /><path stroke-linecap="round" stroke-linejoin="round" d="M18.5 2.5a2.121 2.121 0 013 3L12 15l-4 1 1-4 9.5-9.5z" />`,
  delete: `<path stroke-linecap="round" stroke-linejoin="round" d="M6 7h12M9 7V4a1 1 0 011-1h4a1 1 0 011 1v3m2 0v13a2 2 0 01-2 2H8a2 2 0 01-2-2V7h12z" />`,
  upload: `<path stroke-linecap="round" stroke-linejoin="round" d="M4 16v2a2 2 0 002 2h12a2 2 0 002-2v-2M17 8l-5-5-5 5M12 3v12" />`,
  reopen: `<path stroke-linecap="round" stroke-linejoin="round" d="M16.023 9.348h4.992v-.001M2.985 19.644v-4.992m0 0h4.992m-4.993 0l3.181 3.183a8.25 8.25 0 0013.803-3.7M4.031 9.865a8.25 8.25 0 0113.803-3.7l3.181 3.182m0-4.991v4.99" />`,
  link: `<path stroke-linecap="round" stroke-linejoin="round" d="M13.19 8.688a4.5 4.5 0 011.242 7.244l-4.5 4.5a4.5 4.5 0 01-6.364-6.364l1.757-1.757m13.35-.622l1.757-1.757a4.5 4.5 0 00-6.364-6.364l-4.5 4.5a4.5 4.5 0 001.242 7.244" />`,
  copy: `<path stroke-linecap="round" stroke-linejoin="round" d="M15.75 17.25v3.375c0 .621-.504 1.125-1.125 1.125h-9.75a1.125 1.125 0 01-1.125-1.125V7.875c0-.621.504-1.125 1.125-1.125H6.75a9.06 9.06 0 011.5.124m7.5 10.376h3.375c.621 0 1.125-.504 1.125-1.125V11.25a9 9 0 00-9-9H8.625c-.621 0-1.125.504-1.125 1.125v1.5m7.5 10.375H8.625a1.125 1.125 0 01-1.125-1.125v-9.25" />`,
  request: `<path stroke-linecap="round" stroke-linejoin="round" d="M9 12h3.75M9 15h3.75M9 18h3.75M3.75 4.5h16.5a1.5 1.5 0 011.5 1.5v13.5a1.5 1.5 0 01-1.5 1.5H3.75a1.5 1.5 0 01-1.5-1.5V6a1.5 1.5 0 011.5-1.5z" />`,
  chevronDown: `<path stroke-linecap="round" stroke-linejoin="round" d="M19.5 8.25l-7.5 7.5-7.5-7.5" />`,
  chevronUp: `<path stroke-linecap="round" stroke-linejoin="round" d="M4.5 15.75l7.5-7.5 7.5 7.5" />`,
  qr: `<path stroke-linecap="round" stroke-linejoin="round" d="M3.75 4.5h6v6h-6v-6zM14.25 4.5h6v6h-6v-6zM3.75 14.25h6v6h-6v-6zM14.25 14.25h2.25v2.25M18.75 14.25H20.25M14.25 18.75h2.25M18.75 17.25V20.25M6.75 7.5v0M17.25 7.5v0M6.75 17.25v0" />`
} as const;

export type IconName = keyof typeof ICONS;
