import { defineConfig } from 'vite';
import path from 'path';
import fs from 'fs';
import { fileURLToPath } from 'url';

// Get __dirname equivalent in ES modules
const __dirname = path.dirname(fileURLToPath(import.meta.url));

// Path to Vaadin Lumo styles in node_modules
const lumoPath = path.resolve(__dirname, 'node_modules/@vaadin/vaadin-lumo-styles');

// Custom plugin to serve Lumo CSS from node_modules during development
// and copy it to dist during build
function vaadinLumoPlugin() {
  return {
    name: 'vaadin-lumo-plugin',

    // Serve files from node_modules during development
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        if (req.url?.startsWith('/vaadin-theme/')) {
          const relativePath = req.url.replace('/vaadin-theme/', '');
          let filePath;

          // Map lumo.css to dist/lumo.css (pre-built version)
          if (relativePath === 'lumo.css') {
            filePath = path.join(lumoPath, 'dist', relativePath);
          } else {
            filePath = path.join(lumoPath, relativePath);
          }

          if (fs.existsSync(filePath)) {
            const stat = fs.statSync(filePath);
            if (stat.isFile()) {
              const content = fs.readFileSync(filePath, 'utf-8');
              res.setHeader('Content-Type', 'text/css');
              res.end(content);
              return;
            }
          }
        }
        next();
      });
    },

    // Copy files to dist during build
    closeBundle() {
      const destDir = path.resolve(__dirname, 'dist/vaadin-theme');

      // Ensure dest directory exists
      fs.mkdirSync(destDir, { recursive: true });
      fs.mkdirSync(path.join(destDir, 'src'), { recursive: true });
      fs.mkdirSync(path.join(destDir, 'components'), { recursive: true });

      // Copy lumo.css
      fs.copyFileSync(
        path.join(lumoPath, 'dist/lumo.css'),
        path.join(destDir, 'lumo.css')
      );

      // Copy src folder (recursively)
      copyDirRecursive(path.join(lumoPath, 'src'), path.join(destDir, 'src'));

      // Copy components folder (recursively)
      copyDirRecursive(path.join(lumoPath, 'components'), path.join(destDir, 'components'));

      console.log('Copied Vaadin Lumo styles to dist/vaadin-theme');
    }
  };
}

// Helper function to copy directory recursively
function copyDirRecursive(src, dest) {
  if (!fs.existsSync(src)) return;

  fs.mkdirSync(dest, { recursive: true });
  const entries = fs.readdirSync(src, { withFileTypes: true });

  for (const entry of entries) {
    const srcPath = path.join(src, entry.name);
    const destPath = path.join(dest, entry.name);

    if (entry.isDirectory()) {
      copyDirRecursive(srcPath, destPath);
    } else {
      fs.copyFileSync(srcPath, destPath);
    }
  }
}

export default defineConfig({
  plugins: [
    vaadinLumoPlugin()
  ],
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
  }
});
