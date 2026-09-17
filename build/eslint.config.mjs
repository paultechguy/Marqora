// Copyright (c) 2026 Paul Carver
// SPDX-License-Identifier: Apache-2.0

/*
  What Test-WebShell.ps1 checks the preview shell against.

  One rule, deliberately. no-undef answers the single question the C# build answers for
  everything else in this repository and cannot answer here: does this name exist where it is
  used? In a five-thousand-line file of nested function scopes that is the mistake that actually
  happens, and JavaScript does not find out until the branch runs - which for a branch behind a
  menu toggle can be weeks.

  It is not a style pass and should not become one. The shell's formatting is a matter for the
  person writing it, and a config that started arguing about quotes would earn itself a
  reputation for noise and then be ignored when it had something worth saying.

  The globals are listed by hand rather than pulled from the `globals` package, so this needs
  nothing but eslint itself.
*/

const browser = {};

for (const name of [
  'window', 'document', 'navigator', 'console', 'location', 'history', 'screen',
  'setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'queueMicrotask',
  'requestAnimationFrame', 'cancelAnimationFrame', 'performance',
  'getComputedStyle', 'matchMedia', 'getSelection', 'CSS',
  'URL', 'URLSearchParams', 'Blob', 'File', 'FileReader', 'Image', 'FormData',
  'XMLHttpRequest', 'fetch', 'Headers', 'Request', 'Response', 'AbortController',
  'MutationObserver', 'ResizeObserver', 'IntersectionObserver',
  'DOMParser', 'XMLSerializer', 'Node', 'Element', 'HTMLElement', 'SVGElement',
  'Event', 'CustomEvent', 'KeyboardEvent', 'MouseEvent', 'Range', 'NodeFilter',
  'atob', 'btoa', 'TextEncoder', 'TextDecoder', 'structuredClone', 'crypto',
  'localStorage', 'sessionStorage', 'alert', 'confirm', 'prompt',
]) {
  browser[name] = 'readonly';
}

export default [
  {
    files: ['**/*.js'],
    languageOptions: {
      ecmaVersion: 2021,

      // Plain scripts, not modules. The shell is loaded by <script> tags and shares one
      // namespace on purpose; saying so is what lets the rule see across the files.
      sourceType: 'script',
      globals: {
        ...browser,

        // Loaded from webshell/vendor by the page before app.js runs. Named here because the
        // rule cannot see a script tag.
        monaco: 'readonly',
        require: 'readonly',
        katex: 'readonly',
        mermaid: 'readonly',
        hljs: 'readonly',

        // The host's end of the bridge, injected by WebView2.
        chrome: 'readonly',
      },
    },
    rules: {
      'no-undef': 'error',
    },
  },
];
