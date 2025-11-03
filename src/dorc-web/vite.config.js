import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    rollupOptions: {
      input: {
        index: 'index.html',
        signin: 'signin.html',
        signinCallback: 'signin-callback.html',
        signoutCallback: 'signout-callback.html'
      }
    }
  },
  server: {
    port: 8888
  },
  test: {
    globals: true,
    environment: 'happy-dom',
    include: ['src/**/*.test.ts', 'src/**/*.spec.ts']
  }
});