import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { build } from 'esbuild';
import { pathToFileURL } from 'node:url';
import os from 'node:os';
import path from 'node:path';
import { mkdtemp, rm } from 'node:fs/promises';

test('all six actual geometry groups have finite bounds and selectable parent metadata', async () => {
  // Bundle JSON material import with the same builder used for production.
  const temp = await mkdtemp(path.join(os.tmpdir(), 'my-system-model-'));
  try {
    const target = path.join(temp, 'model.mjs');
    await build({ entryPoints: [new URL('../../../assets/3d-models/hardware.js', import.meta.url).pathname], outfile: target, platform: 'node', format: 'esm', bundle: true,
      nodePaths: [new URL('../node_modules', import.meta.url).pathname] });
    const { createHardware } = await import(pathToFileURL(target));
    const model = createHardware();
    assert.deepEqual(Object.keys(model.groups).sort(), ['cooling', 'cpu', 'gpu', 'memory', 'motherboard', 'storage']);
    let meshes = 0;
    for (const [name, group] of Object.entries(model.groups)) {
      assert.equal(group.userData.component, name);
      assert.ok(group.children.length > 0);
      group.traverse(object => {
        if (!object.isMesh) return;
        meshes++; object.geometry.computeBoundingBox();
        for (const coordinate of object.geometry.boundingBox.min.toArray().concat(object.geometry.boundingBox.max.toArray())) assert.ok(Number.isFinite(coordinate));
      });
    }
    assert.ok(meshes > 300, 'geometry must include real mechanical detail');
  } finally { await rm(temp, { recursive: true, force: true }); }
});
test('production page disables network and has no remote scripts or inline handlers', async () => {
  const html = await readFile(new URL('../index.html', import.meta.url), 'utf8');
  assert.ok(html.includes("connect-src 'none'"));
  assert.ok(html.includes("script-src 'self'"));
  assert.ok(html.includes("style-src 'self'"));
  assert.ok(!/<script[^>]+src=["']https?:/i.test(html));
  assert.ok(!/\son(?:load|click|error)=/i.test(html));
});
