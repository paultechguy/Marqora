/*
  Boot script for the diagram sandbox.

  Kept in its own file rather than inline so the frame's Content-Security-Policy can stay at
  script-src 'self' with no 'unsafe-inline'.

  Failures inside a frame never reach the shell's error handler, so they are recorded on the
  frame's window for the shell's poll loop to collect and forward to the host log.
*/

(function () {
  'use strict';

  window.addEventListener('error', function (e) {
    window.mermaidError = e.message + (e.filename ? ' (' + e.filename + ':' + e.lineno + ')' : '');
  });

  window.addEventListener('unhandledrejection', function (e) {
    var reason = e.reason || {};
    window.mermaidError = reason.message || String(reason);
  });

  // The shell polls for mermaidReady, then drives mermaid directly across the same-origin
  // boundary. No postMessage protocol is needed for a frame in the same origin.
  import('./vendor/mermaid/mermaid.esm.min.mjs').then(function (module) {
    window.mermaid = module.default;
    window.mermaidReady = true;
  }).catch(function (err) {
    window.mermaidError = 'Import failed: ' + (err && err.message ? err.message : String(err));
  });
}());
