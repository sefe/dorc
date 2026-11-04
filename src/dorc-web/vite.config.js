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
          const candidatePath =
            relativePath === 'lumo.css'
              ? path.resolve(lumoPath, 'dist', relativePath)
              : path.resolve(lumoPath, relativePath);

          if (candidatePath.startsWith(lumoPath)) {
            let fd;
            try {
              fd = fs.openSync(candidatePath, 'r');
              const stat = fs.fstatSync(fd);
              if (stat.isFile()) {
                const content = fs.readFileSync(fd, 'utf-8');
                res.setHeader('Content-Type', 'text/css');
                res.end(content);
                return;
              }
            } catch {
              // File does not exist or cannot be opened; fall through to next()
            } finally {
              if (fd !== undefined) {
                fs.closeSync(fd);
              }
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
  const resolvedSrc = path.resolve(src);
  const resolvedDest = path.resolve(dest);
  const allowedDestRoot = path.resolve(__dirname, 'dist/vaadin-theme');
  if (
    !resolvedSrc.startsWith(lumoPath) ||
    !resolvedDest.startsWith(allowedDestRoot) ||
    !fs.existsSync(resolvedSrc)
  ) {
    return;
  }

  fs.mkdirSync(resolvedDest, { recursive: true });
  const entries = fs.readdirSync(resolvedSrc, { withFileTypes: true });

  for (const entry of entries) {
    const safeName = path.basename(entry.name);
    if (!safeName || safeName === '.' || safeName === '..' || safeName !== entry.name) {
      continue;
    }
    const srcPath = path.resolve(resolvedSrc, safeName);
    const destPath = path.resolve(resolvedDest, safeName);
    if (!srcPath.startsWith(resolvedSrc) || !destPath.startsWith(resolvedDest)) {
      continue;
    }

    if (entry.isDirectory()) {
      copyDirRecursive(srcPath, destPath);
    } else {
      fs.copyFileSync(srcPath, destPath);
    }
  }
}

export default defineConfig({
  plugins: [vaadinLumoPlugin()],
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
