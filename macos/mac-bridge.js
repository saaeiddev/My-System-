// Local native-message adapter. No generic script-to-host APIs.
(() => {
  "use strict";
  const listeners = new Set();
  window.chrome = { webview: {
    addEventListener(kind, callback) { if (kind === "message" && typeof callback === "function") listeners.add(callback); },
    removeEventListener(kind, callback) { if (kind === "message") listeners.delete(callback); },
    postMessage(message) {
      if (message && typeof message.type === "string") {
        try { window.webkit?.messageHandlers?.mysystem?.postMessage(message); } catch {}
      }
    }
  }};
  window.__mySystemNativeReceive = data => {
    if (!data || !["telemetry", "config", "select"].includes(data.type)) return;
    for (const fn of listeners) { try { fn({data}); } catch (error) { console.warn(error); } }
  };
})();
