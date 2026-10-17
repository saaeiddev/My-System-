// Original procedural geometry, authored for My System. One scene unit is 50 mm.
// This is a generic ATX engineering illustration, not detected hardware geometry.
import * as THREE from 'three';
import palette from '../materials/palette.json';

export function createHardware() {
  const root = new THREE.Group();
  const groups = {};
  const materials = Object.fromEntries(Object.entries(palette).map(([key, config]) =>
    [key, new THREE.MeshStandardMaterial(config)]));
  const geometryCache = new Map();
  function geometry(key, factory) {
    if (!geometryCache.has(key)) geometryCache.set(key, factory());
    return geometryCache.get(key);
  }
  function group(name) {
    const g = new THREE.Group(); g.name = name; g.userData.component = name;
    root.add(g); groups[name] = g; return g;
  }
  function box(parent, size, position, material = 'graphite') {
    const mesh = new THREE.Mesh(geometry(`box:${size}`, () => new THREE.BoxGeometry(...size)), materials[material]);
    mesh.position.set(...position); mesh.castShadow = true; mesh.receiveShadow = true;
    parent.add(mesh); return mesh;
  }
  function cylinder(parent, radius, height, position, material = 'titanium', segments = 20) {
    const mesh = new THREE.Mesh(geometry(`cylinder:${radius}:${height}:${segments}`,
      () => new THREE.CylinderGeometry(radius, radius, height, segments)), materials[material]);
    mesh.position.set(...position); mesh.castShadow = true; mesh.receiveShadow = true;
    parent.add(mesh); return mesh;
  }
  function ring(parent, radius, thickness, position, material = 'graphite') {
    const mesh = new THREE.Mesh(geometry(`ring:${radius}:${thickness}`,
      () => new THREE.TorusGeometry(radius, thickness, 8, 48)), materials[material]);
    mesh.position.set(...position); mesh.rotation.x = -Math.PI / 2;
    parent.add(mesh); return mesh;
  }
  function screw(parent, x, y, z) {
    cylinder(parent, .055, .04, [x, y, z], 'titanium', 8);
    box(parent, [.065, .006, .012], [x, y + .023, z], 'black');
  }
  function fan(parent, radius, at, rotation = [0, 0, 0]) {
    const frame = new THREE.Group(); frame.position.set(...at); frame.rotation.set(...rotation); parent.add(frame);
    const edge = radius + .1;
    for (const x of [-edge, edge]) box(frame, [.11, .16, edge * 2 + .11], [x, 0, 0]);
    for (const z of [-edge, edge]) box(frame, [edge * 2, .16, .11], [0, 0, z]);
    ring(frame, radius, .065, [0, 0, 0], 'black');
    const rotor = new THREE.Group(); frame.add(rotor);
    cylinder(rotor, radius * .24, .17, [0, .04, 0], 'titanium');
    cylinder(rotor, radius * .15, .18, [0, .045, 0], 'black');
    for (let i = 0; i < 9; i++) {
      const blade = new THREE.Group(); blade.rotation.y = i * Math.PI * 2 / 9;
      const mesh = box(blade, [radius * .32, .045, radius * .7], [radius * .15, .02, radius * .57], 'black');
      mesh.rotation.y = -.36; mesh.rotation.z = .2; rotor.add(blade);
    }
    for (const x of [-edge, edge]) for (const z of [-edge, edge]) screw(frame, x, .1, z);
    return rotor;
  }

  const motherboard = group('motherboard');
  box(motherboard, [5.9, .09, 6.7], [0, -.12, 0], 'pcb');
  // Routed copper traces, socket positions, IO panel and edge power sockets.
  for (let i = 0; i < 24; i++) {
    const z = -2.85 + i * .23;
    box(motherboard, [1.0 + (i % 4) * .18, .006, .012], [-1.9, -.07, z], 'fins');
    box(motherboard, [.012, .006, .32], [-1.7 + (i % 4) * .16, -.066, z + .14], 'fins');
  }
  for (const x of [-2.76, 2.76]) for (const z of [-3.1, 0, 3.1]) screw(motherboard, x, -.03, z);
  box(motherboard, [.55, .48, 2.25], [-2.65, .15, -1.7], 'titanium');
  for (let i = 0; i < 6; i++) box(motherboard, [.56, .16, .22], [-2.66, .17, -2.55 + i * .34], 'black');
  box(motherboard, [.52, .38, 1.5], [2.63, .1, -.5], 'black');
  for (let i = 0; i < 12; i++) box(motherboard, [.11, .025, .045], [2.55, .3, -1.15 + i * .11], 'gold');
  for (let i = 0; i < 9; i++) {
    cylinder(motherboard, .085, .21, [-2.05 + (i % 3) * .26, .08, -2.7 + Math.floor(i / 3) * .28]);
    box(motherboard, [.16, .08, .2], [-1.96 + i * .36, .02, -3.06], 'black');
  }
  box(motherboard, [1.18, .25, .9], [-1.8, .09, -1.2], 'graphite');
  for (let i = 0; i < 12; i++) box(motherboard, [.05, .12, .85], [-2.32 + i * .095, .25, -1.2], 'fins');
  box(motherboard, [1.45, .18, 1], [.55, .03, 2.53], 'graphite');
  for (let i = 0; i < 5; i++) box(motherboard, [.08, .03, .8], [.05 + i * .23, .14, 2.53], 'titanium');
  for (const z of [.58, 1.55]) {
    box(motherboard, [3.6, .22, .16], [-.48, .04, z], 'black');
    box(motherboard, [3.2, .03, .025], [-.58, .16, z], 'gold');
  }
  cylinder(motherboard, .24, .055, [-1.94, .01, 2.45], 'titanium', 32);
  for (let i = 0; i < 4; i++) box(motherboard, [.26, .22, .32], [2.4, .03, 1.7 + i * .38], 'black');
  for (let i = 0; i < 10; i++) box(motherboard, [.18, .07, .12], [-2.5 + i * .38, -.015, 3.05], 'black');

  const cpu = group('cpu');
  box(cpu, [1.2, .12, 1.2], [-.55, .015, -1.55], 'black');
  box(cpu, [1.02, .08, 1.02], [-.55, .11, -1.55], 'pcb');
  const cpuLid = box(cpu, [.89, .085, .89], [-.55, .19, -1.55], 'titanium');
  for (const x of [-1.14, .04]) box(cpu, [.035, .05, 1.17], [x, .15, -1.55], 'titanium');
  box(cpu, [.035, .04, 1.4], [.12, .17, -1.48], 'titanium').rotation.y = .08;
  const cpuIndicator = box(cpu, [.5, .014, .035], [-.55, .238, -1.55], 'teal');

  const cooling = group('cooling');
  const cooler = new THREE.Group(); cooling.add(cooler);
  box(cooler, [1.02, .14, 1.02], [-.55, .31, -1.55], 'copper');
  for (const x of [-.91, -.67, -.43, -.19]) {
    cylinder(cooler, .046, 1.45, [x, 1, -1.85], 'copper', 12);
    cylinder(cooler, .046, 1.45, [x, 1, -1.25], 'copper', 12);
  }
  for (let i = 0; i < 22; i++) box(cooler, [1.65, .035, 1.35], [-.55, .54 + i * .055, -1.55], 'fins');
  box(cooler, [1.68, .065, 1.38], [-.55, 1.77, -1.55], 'graphite');
  for (const x of [-1.1, 0]) box(cooler, [.055, .014, 1.1], [x, 1.809, -1.55], 'titanium');
  fan(cooler, .68, [-.55, 1.1, -.76], [Math.PI / 2, 0, 0]);
  // Separate system fan rail, as on an open hardware test bench.
  box(cooling, [.16, 2.15, 4.35], [-3.75, .85, -.2], 'graphite');
  fan(cooling, .88, [-3.6, 1.01, -1.3], [0, 0, Math.PI / 2]);
  fan(cooling, .88, [-3.6, 1.01, .95], [0, 0, Math.PI / 2]);

  const memory = group('memory');
  for (let n = 0; n < 4; n++) {
    const x = 1.05 + n * .36;
    box(memory, [.2, .16, 2.76], [x, .02, -1.55], 'black');
    for (const z of [-2.98, -.12]) box(memory, [.24, .26, .15], [x, .08, z], 'titanium');
    if (n % 2 === 0) continue;
    box(memory, [.055, .82, 2.6], [x, .48, -1.55], 'pcb');
    box(memory, [.1, .11, 2.43], [x, .13, -1.55], 'gold');
    for (let side of [-1, 1]) for (let i = 0; i < 8; i++)
      box(memory, [.055, .33, .24], [x + side * .06, .5, -2.65 + i * .31], 'black');
    box(memory, [.18, .075, 2.64], [x, .94, -1.55], 'titanium');
    box(memory, [.13, .015, 2.2], [x, .986, -1.55], 'teal');
  }

  const gpu = group('gpu');
  box(gpu, [4.75, 1.48, .08], [-.35, .99, .6], 'pcb');
  box(gpu, [3.1, .16, .05], [-.56, .19, .6], 'gold');
  box(gpu, [4.82, 1.51, .065], [-.35, .99, .52], 'graphite');
  for (let i = 0; i < 44; i++) box(gpu, [.035, 1.27, .65], [-2.6 + i * .105, 1.02, .98], 'fins');
  for (const y of [.37, 1.65]) box(gpu, [4.82, .13, .94], [-.35, y, 1], 'graphite');
  for (const x of [-2.76, 2.06]) box(gpu, [.14, 1.3, .94], [x, 1.01, 1], 'graphite');
  for (const x of [-1.75, -.34, 1.07]) fan(gpu, .57, [x, 1.02, 1.51], [Math.PI / 2, 0, 0]);
  box(gpu, [3.9, .028, .13], [-.35, 1.727, 1.28], 'teal');
  box(gpu, [.075, 1.8, 1.1], [-2.83, .95, .92], 'titanium');
  for (let i = 0; i < 3; i++) box(gpu, [.08, .19, .34], [-2.875, .6 + i * .4, 1.12], 'black');
  for (const x of [-1.45, -.5, .45]) box(gpu, [.55, .4, .03], [x, .92, .465], 'black');
  const gpuIndicator = box(gpu, [.4, .12, .014], [1.7, 1.45, 1.482], 'teal');

  const storage = group('storage');
  // M.2 2280 board and removable-style heatsink with exposed controller/NAND.
  box(storage, [.49, .055, 1.64], [1.7, .015, 2.27], 'pcb');
  for (const z of [1.72, 2.15, 2.58]) box(storage, [.37, .06, .3], [1.7, .075, z], 'black');
  screw(storage, 1.7, .055, 3.02);
  box(storage, [.38, .02, .15], [1.7, .05, 1.48], 'gold');
  // 2.5 inch SATA drive in a separate tray beside the motherboard.
  box(storage, [1.45, .18, 2.1], [4.08, .04, 1.35], 'titanium');
  box(storage, [1.32, .025, 1.96], [4.08, .144, 1.35], 'graphite');
  box(storage, [.52, .012, 1.42], [3.78, .164, 1.35], 'black');
  for (let i = 0; i < 5; i++) box(storage, [.38, .013, .025], [4.35, .166, .87 + i * .16], 'titanium');
  box(storage, [.95, .09, .13], [4.03, .04, .26], 'black');
  for (const x of [3.46, 4.7]) for (const z of [.42, 2.28]) screw(storage, x, .174, z);
  const cableCurve = new THREE.CatmullRomCurve3([
    new THREE.Vector3(4.05, .05, .2), new THREE.Vector3(4.03, -.05, -.32),
    new THREE.Vector3(3.12, -.04, -.25), new THREE.Vector3(3.05, .05, 1.78), new THREE.Vector3(2.5, .05, 1.78)
  ]);
  const cable = new THREE.Mesh(new THREE.TubeGeometry(cableCurve, 32, .045, 6, false), materials.black);
  storage.add(cable);
  // Group metadata is inherited through raycast ancestor traversal.
  const indicators = { cpu: cpuIndicator, gpu: gpuIndicator };
  for (const indicator of Object.values(indicators)) {
    indicator.material = indicator.material.clone(); indicator.material.emissiveIntensity = .35;
  }
  return { root, groups, cooler, cpuLid, indicators, materials };
}
