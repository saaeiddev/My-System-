import test from 'node:test';
import assert from 'node:assert/strict';
import { availableValue, normalizeMetrics, metricFor, thermalLevel, formatMetric, safeConfig } from '../telemetry.mjs';

test('unsupported or malformed values never turn into invented zero readings', () => {
  for (const metric of [null, {}, { value: null, availability: 'Available' }, { value: NaN, availability: 0 },
    { value: 83, availability: 'Unavailable' }, { value: 83, availability: 'Error' }, { value: '83', availability: 0 }]) {
    assert.equal(availableValue(metric), null); assert.equal(formatMetric(metric), 'N/A');
  }
  assert.equal(availableValue({ value: 0, availability: 'Available' }), 0);
  assert.equal(availableValue({ value: 0, availability: 0 }), 0);
});
test('thermal highlighting requires a real supported Celsius measurement', () => {
  assert.equal(thermalLevel({ value: 85, unit: '°C', availability: 'available' }, 85), 1);
  assert.equal(thermalLevel({ value: 85, unit: '°C', availability: 'Unavailable' }), null);
  assert.equal(thermalLevel({ value: 185, unit: '°F', availability: 'Available' }), null);
  assert.equal(thermalLevel({ value: 10, unit: '°C', availability: 'Available' }), 0);
  assert.equal(thermalLevel({ value: 85, unit: '°C', availability: 'Available' }, 95), 25 / 35);
});
test('selection routes exact native metric domains and respects sensor state', () => {
  const metrics = normalizeMetrics([
    { id: 'cpu.usage', value: 27, unit: '%', availability: 'Available' },
    { id: 'cpu.temperature', value: 45, unit: '°C', availability: 'Unavailable' },
    { id: 'memory.used', value: 4.8, unit: 'GB', availability: 0 },
    { id: 'disk.activity', value: 0, unit: '%', availability: 'Available' }
  ]);
  assert.equal(metricFor(metrics, 'cpu', 'load').value, 27);
  assert.equal(metricFor(metrics, 'cpu', 'temperature').value, null);
  assert.equal(metricFor(metrics, 'disk', 'activity').value, 0);
  assert.equal(metricFor(metrics, 'memory', 'used').value, 4.8);
  assert.equal(metricFor(metrics, 'gpu', 'load'), null);
});
test('untrusted bridge configuration is bounded and cannot inject CSS', () => {
  const config = { quality: 'medium', fpsCap: 30, accent: '#50d9c3', paused: false };
  assert.deepEqual(safeConfig({ fpsCap: 900, quality: 'ultra', accent: 'red;url(https://bad)', paused: 'true' }, config), { ...config, fpsCap: 60 });
  assert.equal(safeConfig({ fpsCap: 0 }, config).fpsCap, 5);
  assert.equal(safeConfig({ accent: '#ABC123', paused: true }, config).accent, '#ABC123');
});
