import * as THREE from 'three';
import { OrbitControls } from 'three/addons/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/addons/environments/RoomEnvironment.js';
import { createHardware } from '../../assets/3d-models/hardware.js';
import { COMPONENTS, normalizeMetrics, metricFor, formatMetric, thermalLevel, safeConfig } from './telemetry.mjs';

const $ = id => document.getElementById(id);
const metadata = {
  overview: ['FULL SYSTEM / 00', 'Built to explore.', 'Select a component to inspect its live measurements.', 'CPU utilization', 'cpu', 'load'],
  cpu: ['PROCESSOR / 01', 'Compute, in focus.', 'The processor package, retention bracket and heat spreader.', 'CPU temperature', 'cpu', 'temperature'],
  gpu: ['GRAPHICS / 02', 'Parallel precision.', 'A triple-fan graphics card with fin array and full-length backplate.', 'GPU utilization', 'gpu', 'load'],
  memory: ['MEMORY / 03', 'Room to perform.', 'Two memory modules seated in a four-slot DIMM bank.', 'Physical memory used', 'memory', 'used'],
  storage: ['STORAGE / 04', 'Every byte, placed.', 'An M.2 module and a 2.5-inch SATA drive with data connection.', 'Disk activity', 'disk', 'activity'],
  motherboard: ['MAINBOARD / 05', 'The foundation.', 'ATX board, rear I/O, expansion slots and power delivery.', 'Board temperature', 'motherboard', 'temperature'],
  cooling: ['THERMALS / 06', 'Made to dissipate.', 'A copper heat-pipe tower and two system fans. Fans are shown stationary.', 'CPU temperature', 'cpu', 'temperature']
};
let config = { quality: 'high', fpsCap: 30, reducedMotion: matchMedia('(prefers-reduced-motion: reduce)').matches, paused: false, appearance: 'dark', accent: '#50d9c3' };
let metrics = new Map(), thresholds = { cpu: 85, gpu: 83 };
let selected = 'overview', isolated = false, exploded = false, hostPaused = false, localPaused = false;
let renderer, scene, camera, controls, model, grid, selectionBox, animationId = 0, timerId = 0, lastRender = 0, disposed = false, dirty = true;
let lastTelemetry = 0;
const home = new THREE.Vector3(9.5, 12.4, 12.6);
const target = new THREE.Vector3(.2, .25, .15);
const pointer = new THREE.Vector2(), raycaster = new THREE.Raycaster();
function send(message) { window.chrome?.webview?.postMessage(message); }
function blocked() { return disposed || document.hidden || hostPaused || localPaused; }
function invalidate() {
  dirty = true;
  if (!animationId && !timerId && !blocked() && renderer) animationId = requestAnimationFrame(render);
}
function render(now) {
  animationId = 0;
  if (blocked()) return;
  const interval = 1000 / config.fpsCap;
  if (now - lastRender < interval) {
    timerId = setTimeout(() => { timerId = 0; invalidate(); }, interval - (now - lastRender));
    return;
  }
  lastRender = now;
  const moved = controls.update();
  if (dirty || moved) { renderer.render(scene, camera); dirty = false; }
  if (moved) invalidate();
}
function updatePause() {
  $('pause').setAttribute('aria-pressed', String(localPaused));
  $('pause').textContent = localPaused ? 'Resume' : 'Ⅱ Pause';
  $('render-status').textContent = blocked() ? '3D PAUSED' : `ON-DEMAND · ${config.fpsCap} FPS CAP`;
  if (blocked()) { cancelAnimationFrame(animationId); clearTimeout(timerId); animationId = timerId = 0; }
  else invalidate();
}
function measurement() {
  const data = metadata[selected], metric = metricFor(metrics, data[4], data[5]);
  $('metric-label').textContent = data[3];
  $('metric-value').textContent = formatMetric(metric);
  $('metric-status').textContent = metric?.value !== null && metric?.value !== undefined ? 'Live measurement · native provider' : 'N/A — Sensor Not Available';
}
function setSelection(component, notify = true) {
  if (!COMPONENTS.includes(component)) return;
  selected = component;
  if (selected === 'overview') isolated = false;
  document.querySelectorAll('[data-component]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.component === selected)));
  const data = metadata[selected];
  $('selection-index').textContent = data[0]; $('selection-title').textContent = data[1]; $('selection-description').textContent = data[2];
  $('isolate').disabled = selected === 'overview';
  applyIsolation(); measurement();
  $('announcement').textContent = `${selected === 'overview' ? 'Full system' : selected} selected`;
  if (notify) send({ type: 'select', component: selected });
}
function applyIsolation() {
  if (!model) return;
  for (const [name, group] of Object.entries(model.groups)) group.visible = !isolated || name === selected || (selected === 'cpu' && name === 'cooling');
  // For CPU isolation, include the tower but hide the separate chassis fan rail.
  if (isolated && selected === 'cpu') model.groups.cooling.children.forEach(child => { child.visible = child === model.cooler; });
  else model.groups.cooling.children.forEach(child => { child.visible = true; });
  $('isolate').setAttribute('aria-pressed', String(isolated));
  selectionBox.visible = selected !== 'overview';
  if (selectionBox.visible) {
    const bounds = new THREE.Box3().setFromObject(model.groups[selected]);
    selectionBox.box.copy(bounds.expandByScalar(.09));
  }
  invalidate();
}
function reset() {
  isolated = false; exploded = false; model.cooler.position.y = 0; model.cpuLid.position.y = .19;
  $('explode').setAttribute('aria-pressed', 'false');
  controls.target.copy(target); camera.position.copy(home); controls.update(); setSelection('overview');
}
function zoom(amount) { camera.position.sub(controls.target).multiplyScalar(amount).add(controls.target); controls.update(); invalidate(); }
function applyConfig(input) {
  config = safeConfig(input, config);
  if (typeof input.paused === 'boolean') hostPaused = input.paused;
  document.body.classList.toggle('light', config.appearance === 'light');
  // CSSOM property assignment does not inject CSS text or require unsafe-inline.
  document.documentElement.style.setProperty('--accent', config.accent);
  if (renderer) {
    controls.enableDamping = !config.reducedMotion;
    const dark = config.appearance === 'dark';
    scene.background.set(dark ? '#10171c' : '#edf1f2');
    scene.fog.color.copy(scene.background);
    renderer.setPixelRatio(Math.min(devicePixelRatio, { high: 2, medium: 1.5, low: 1 }[config.quality]));
    renderer.shadowMap.enabled = config.quality === 'high';
    selectionBox.material.color.set(config.accent);
    resize();
  }
  updatePause();
}
function resize() {
  const rect = $('viewport').getBoundingClientRect();
  if (!renderer || !rect.width || !rect.height) return;
  camera.aspect = rect.width / rect.height; camera.updateProjectionMatrix(); renderer.setSize(rect.width, rect.height, false); invalidate();
}
function receive(event) {
  const message = event.data;
  if (!message || typeof message !== 'object' || Array.isArray(message)) return;
  if (message.type === 'config') applyConfig(message);
  if (message.type === 'select') setSelection(message.component, false);
  if (message.type === 'telemetry') {
    metrics = normalizeMetrics(message.metrics); lastTelemetry = Date.now();
    if (message.thresholds && typeof message.thresholds === 'object') for (const key of ['cpu', 'gpu'])
      if (Number.isFinite(message.thresholds[key])) thresholds[key] = message.thresholds[key];
    $('connection').textContent = 'NATIVE TELEMETRY CONNECTED'; $('connection').classList.add('live');
    measurement();
    if (model) for (const [component, mesh] of Object.entries(model.indicators)) {
      const level = thermalLevel(metricFor(metrics, component, 'temperature'), thresholds[component]);
      const color = level === null ? new THREE.Color('#697b85') : new THREE.Color(config.accent).lerp(new THREE.Color(level >= .8 ? '#ef7564' : '#e9b370'), level);
      mesh.material.color.copy(color); mesh.material.emissive.copy(color); mesh.material.emissiveIntensity = level === null ? 0 : .22;
    }
    invalidate();
  }
}
function setup() {
  scene = new THREE.Scene(); scene.background = new THREE.Color('#10171c'); scene.fog = new THREE.Fog('#10171c', 22, 45);
  camera = new THREE.PerspectiveCamera(35, 1, .1, 100); camera.position.copy(home);
  renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'low-power', alpha: false });
  renderer.outputColorSpace = THREE.SRGBColorSpace; renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.22; renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  $('viewport').appendChild(renderer.domElement);
  const generator = new THREE.PMREMGenerator(renderer); const room = new RoomEnvironment();
  scene.environment = generator.fromScene(room, .04).texture; room.dispose(); generator.dispose();
  scene.add(new THREE.HemisphereLight('#d8f5ff', '#263037', 2.1));
  const key = new THREE.DirectionalLight('#f5f3ee', 4.5); key.position.set(-3, 9, 5); key.castShadow = true;
  key.shadow.mapSize.set(1024, 1024); key.shadow.camera.left = -8; key.shadow.camera.right = 8; key.shadow.camera.top = 8; key.shadow.camera.bottom = -8; key.shadow.normalBias = .03; scene.add(key);
  const fill = new THREE.DirectionalLight('#7dd7d0', 1.2); fill.position.set(7, 5, -4); scene.add(fill);
  const ground = new THREE.Mesh(new THREE.PlaneGeometry(200, 200), new THREE.ShadowMaterial({ color: '#000000', opacity: .3 }));
  ground.rotation.x = -Math.PI / 2; ground.position.y = -.38; ground.receiveShadow = true; scene.add(ground);
  grid = new THREE.GridHelper(40, 80, '#31464e', '#24333b'); grid.position.y = -.37;
  grid.material.transparent = true; grid.material.opacity = .43; scene.add(grid);
  model = createHardware(); scene.add(model.root);
  selectionBox = new THREE.Box3Helper(new THREE.Box3(), new THREE.Color(config.accent));
  selectionBox.material.transparent = true; selectionBox.material.opacity = .44; selectionBox.visible = false; scene.add(selectionBox);
  controls = new OrbitControls(camera, renderer.domElement); controls.target.copy(target);
  controls.enableDamping = !config.reducedMotion; controls.dampingFactor = .12;
  controls.minDistance = 3; controls.maxDistance = 35; controls.maxPolarAngle = Math.PI * .49;
  controls.addEventListener('change', invalidate); controls.addEventListener('start', invalidate); controls.update();
  let down = null;
  renderer.domElement.addEventListener('pointerdown', event => { down = [event.clientX, event.clientY, event.button]; });
  renderer.domElement.addEventListener('pointerup', event => {
    if (!down || down[2] !== 0 || Math.hypot(event.clientX - down[0], event.clientY - down[1]) > 5) return;
    const rect = renderer.domElement.getBoundingClientRect();
    pointer.set((event.clientX - rect.left) / rect.width * 2 - 1, -(event.clientY - rect.top) / rect.height * 2 + 1);
    raycaster.setFromCamera(pointer, camera);
    const hit = raycaster.intersectObjects(Object.values(model.groups).filter(group => group.visible), true).find(hit => {
      for (let object = hit.object; object; object = object.parent) if (!object.visible) return false;
      return true;
    });
    if (hit) { let object = hit.object; while (object && !object.userData.component) object = object.parent; if (object) setSelection(object.userData.component); }
    down = null;
  });
  renderer.domElement.addEventListener('webglcontextlost', event => { event.preventDefault(); hostPaused = true; updatePause(); $('error').hidden = false; send({ type: 'rendererError', reason: 'context-lost' }); });
  renderer.domElement.addEventListener('webglcontextrestored', () => { hostPaused = config.paused; $('error').hidden = true; updatePause(); });
  new ResizeObserver(resize).observe($('viewport')); applyConfig(config);
  // Paint one initial frame before settling into on-demand/visibility scheduling.
  // This also gives a hidden native host a valid first surface on activation.
  renderer.render(scene, camera); dirty = false; invalidate();
  send({ type: 'ready', version: 1 });
}
document.querySelectorAll('[data-component]').forEach(button => button.addEventListener('click', () => setSelection(button.dataset.component)));
$('reset').addEventListener('click', () => { if (model) reset(); });
$('isolate').addEventListener('click', () => { isolated = !isolated; applyIsolation(); });
$('explode').addEventListener('click', () => {
  if (!model) return;
  exploded = !exploded; model.cooler.position.y = exploded ? 1.9 : 0; model.cpuLid.position.y = exploded ? .8 : .19;
  $('explode').setAttribute('aria-pressed', String(exploded)); setSelection('cpu');
});
$('zoom-in').addEventListener('click', () => { if (controls) zoom(.86); });
$('zoom-out').addEventListener('click', () => { if (controls) zoom(1.16); });
$('pause').addEventListener('click', () => { localPaused = !localPaused; updatePause(); });
$('viewport').addEventListener('keydown', event => {
  if (!controls) return;
  if (event.key.toLowerCase() === 'r') reset();
  else if (event.key.toLowerCase() === 'i' && selected !== 'overview') { isolated = !isolated; applyIsolation(); }
  else if (event.key === '+' || event.key === '=') zoom(.86);
  else if (event.key === '-') zoom(1.16);
  else if (event.key.startsWith('Arrow')) {
    const offset = camera.position.clone().sub(controls.target), spherical = new THREE.Spherical().setFromVector3(offset);
    if (event.key === 'ArrowLeft') spherical.theta -= .1;
    if (event.key === 'ArrowRight') spherical.theta += .1;
    if (event.key === 'ArrowUp') spherical.phi = Math.max(.1, spherical.phi - .1);
    if (event.key === 'ArrowDown') spherical.phi = Math.min(Math.PI * .49, spherical.phi + .1);
    camera.position.copy(controls.target).add(new THREE.Vector3().setFromSpherical(spherical)); controls.update(); invalidate();
  } else return;
  event.preventDefault();
});
document.addEventListener('visibilitychange', updatePause);
window.chrome?.webview?.addEventListener('message', receive);
// No window.postMessage receiver is installed: only WebView2's native bridge can
// provide measurements/configuration. The renderer exposes no host object.
const staleTimer = setInterval(() => {
  if (lastTelemetry && Date.now() - lastTelemetry > 15000) {
    metrics = new Map(); lastTelemetry = 0; measurement();
    $('connection').textContent = 'Telemetry paused or disconnected'; $('connection').classList.remove('live');
    if (model) for (const mesh of Object.values(model.indicators)) { mesh.material.color.set('#697b85'); mesh.material.emissiveIntensity = 0; }
    invalidate();
  }
}, 5000);
window.addEventListener('pagehide', () => {
  disposed = true; cancelAnimationFrame(animationId); clearTimeout(timerId); clearInterval(staleTimer); controls?.dispose();
  if (scene) { const geometries = new Set(), materials = new Set(); scene.traverse(object => { if (object.geometry) geometries.add(object.geometry); if (object.material) for (const m of Array.isArray(object.material) ? object.material : [object.material]) materials.add(m); }); geometries.forEach(g => g.dispose()); materials.forEach(m => m.dispose()); scene.environment?.dispose(); }
  renderer?.dispose();
});
try { setup(); } catch (error) { $('error').hidden = false; send({ type: 'rendererError', reason: error instanceof Error ? error.message : 'WebGL unavailable' }); }
