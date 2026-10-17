import { build } from 'esbuild';
import { mkdir, copyFile, rm } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = path.dirname(fileURLToPath(import.meta.url));
const dist = path.join(root, 'dist');
await rm(dist, { recursive: true, force: true });
await mkdir(dist, { recursive: true });
await build({ entryPoints: [path.join(root, 'scene.js')], bundle: true, minify: true,
  outfile: path.join(dist, 'scene.js'), format: 'esm', target: ['chrome120'],
  nodePaths: [path.join(root, 'node_modules')],
  legalComments: 'eof', sourcemap: false, logLevel: 'info' });
for (const name of ['index.html', 'scene.css']) await copyFile(path.join(root, name), path.join(dist, name));
await copyFile(path.join(root, 'node_modules/three/LICENSE'), path.join(dist, 'THREE-LICENSE.txt'));
console.log('Local WebView2 scene built. No network requests are required at runtime.');
