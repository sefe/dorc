// Vite serves `?raw` imports as the module's source text. The app's own
// `src/typings.d.ts` is outside tsconfig.test.json's `include`, so the test
// project needs its own declaration.
declare module '*?raw' {
  const source: string;
  export default source;
}
