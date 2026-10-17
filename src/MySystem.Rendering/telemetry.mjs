export const COMPONENTS = Object.freeze(['overview', 'cpu', 'gpu', 'memory', 'storage', 'motherboard', 'cooling']);
export function availableValue(metric) {
  if (!metric || typeof metric.value !== 'number' || !Number.isFinite(metric.value)) return null;
  const state = metric.availability;
  // The native bridge uses camel-case string enums. Numeric 0 is accepted for
  // System.Text.Json's default enum representation (Available = 0).
  return state === 0 || (typeof state === 'string' && state.toLowerCase() === 'available') ? metric.value : null;
}
export function normalizeMetrics(metrics) {
  const result = new Map();
  if (!Array.isArray(metrics)) return result;
  for (const metric of metrics.slice(0, 8192)) {
    if (!metric || typeof metric.id !== 'string' || metric.id.length > 256) continue;
    result.set(metric.id, { ...metric, value: availableValue(metric) });
  }
  return result;
}
export function metricFor(metrics, component, kind) {
  const names = {
    load: ['usage', 'load', 'utilization'],
    temperature: ['temperature', 'temp'],
    fan: ['fan', 'rpm'],
    used: ['used', 'usage'],
    activity: ['activity', 'load', 'usage']
  }[kind] ?? [kind];
  const matches = [...metrics.values()].filter(m => {
    const id = m.id.toLowerCase();
    return id.startsWith(component + '.') && names.some(name => id.includes(name));
  });
  // A generic/total/package sensor is preferred; otherwise retain provider order.
  matches.sort((a, b) => Number(/total|package/.test(b.id)) - Number(/total|package/.test(a.id)));
  return matches.find(m => m.value !== null) ?? matches[0] ?? null;
}
export function thermalLevel(metric, threshold = 85) {
  const temperature = availableValue(metric);
  if (temperature === null || !['°c', 'c', 'celsius'].includes((metric.unit ?? '').toLowerCase())) return null;
  return Math.max(0, Math.min(1, (temperature - (threshold - 35)) / 35));
}
export function formatMetric(metric) {
  const value = availableValue(metric);
  if (value === null) return 'N/A';
  const digits = metric.unit === '%' || metric.unit === 'RPM' ? 0 : 1;
  return `${value.toLocaleString(undefined, { maximumFractionDigits: digits })}${metric.unit === '%' || metric.unit === '°C' ? '' : ' '}${metric.unit ?? ''}`.trim();
}
export function safeConfig(input, current) {
  const out = { ...current };
  if (['high', 'medium', 'low'].includes(input.quality)) out.quality = input.quality;
  if (Number.isFinite(input.fpsCap)) out.fpsCap = Math.max(5, Math.min(60, input.fpsCap));
  for (const key of ['reducedMotion', 'paused']) if (typeof input[key] === 'boolean') out[key] = input[key];
  if (['dark', 'light'].includes(input.appearance)) out.appearance = input.appearance;
  if (typeof input.accent === 'string' && /^#[0-9a-f]{6}$/i.test(input.accent)) out.accent = input.accent;
  return out;
}
