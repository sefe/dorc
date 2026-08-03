// Vite's ?raw suffix imports resolve to the file's text content.
declare module '*.css?raw' {
  const content: string;
  export default content;
}
