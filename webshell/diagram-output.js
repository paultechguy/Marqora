/*
  Diagrams drawn for leaving the app.

  Dark mode is a screen setting. Everything that leaves Marqora - a print, a PDF, an HTML
  file, a Folio, a Word document, a copy as PNG, SVG or rich text, a shared review - is
  light, and for text the stylesheet sees to that: the print block and the exporters pin the
  light palette. A diagram cannot be recolored that way, because mermaid bakes its colors
  into the SVG it draws. A light diagram has to be drawn light.

  So this is a second mermaid, in a sandbox frame of its own, configured light once and never
  re-themed. The screen's mermaid follows the app; this one does not follow anything. Two
  instances rather than one switched back and forth, because a switch has to be fenced off
  from every other render that might land while it is in effect, and that fencing was the
  part that did not hold.

  Light is the default, not a rule over the author. A definition that names its own theme -
  an init directive or frontmatter config - is drawn that way here too, because that was a
  choice somebody made on purpose.

  Shared by the preview shell and the cheatsheet, the way diagram-raster.js is shared by the
  shell and the pop-out, so the two pages cannot drift into drawing different pictures.
*/

(function () {
  'use strict';

  var ready = null;
  var options = null;
  var queue = Promise.resolve();
  var seq = 0;

  // By definition. A light drawing of a definition never changes, so nothing here is ever
  // invalidated; what is dropped is only what has not been asked for in a while.
  var cache = new Map();
  var inFlight = new Map();
  var CACHE_LIMIT = 200;

  /*
    The page's own mermaid options, as a function so the font is read when the frame starts
    rather than when the page script first runs. The theme is overridden here; everything
    else - security level, font, sequence wrapping - is the page's, so the two drawings of a
    diagram differ only in color.
  */
  function configure(optionsFn) {
    options = optionsFn;
  }

  function ensure() {
    if (ready) { return ready; }

    ready = new Promise(function (resolve, reject) {
      var frame = document.createElement('iframe');
      frame.className = 'mq-diagram-frame';
      frame.setAttribute('aria-hidden', 'true');
      frame.setAttribute('tabindex', '-1');
      frame.setAttribute('title', 'Diagram sandbox for output');

      var attempts = 0;

      frame.onload = function () {
        (function waitForModule() {
          var win = frame.contentWindow;

          if (win && win.mermaidReady && win.mermaid) {
            var settings = options ? options() : {};
            settings.theme = 'default';
            settings.startOnLoad = false;

            win.mermaid.initialize(settings);
            resolve(win.mermaid);
            return;
          }

          if (win && win.mermaidError) {
            reject(new Error('Output diagram sandbox: ' + win.mermaidError));
            return;
          }

          if (++attempts > 200) {
            reject(new Error('The output diagram sandbox did not finish loading.'));
            return;
          }

          setTimeout(waitForModule, 25);
        }());
      };

      frame.onerror = function () {
        reject(new Error('The output diagram sandbox could not be loaded.'));
      };

      frame.src = 'mermaid-frame.html';
      document.body.appendChild(frame);
    });

    return ready;
  }

  function remember(source, svg) {
    cache.delete(source);
    cache.set(source, svg);

    if (cache.size > CACHE_LIMIT) {
      cache.delete(cache.keys().next().value);
    }
  }

  /// The drawing already made for this definition, or null.
  function cached(source) {
    if (!cache.has(source)) { return null; }

    var svg = cache.get(source);
    remember(source, svg);
    return svg;
  }

  /*
    Resolves with the light SVG markup for a definition; rejects when it will not parse.

    One render at a time, because mermaid keeps a single working area per frame. A definition
    already being drawn is not drawn twice: the preview asks again on every keystroke that
    does not touch the diagram, and those all wait on the one render.
  */
  function render(source) {
    var hit = cached(source);
    if (hit !== null) { return Promise.resolve(hit); }

    if (inFlight.has(source)) { return inFlight.get(source); }

    var job = ensure().then(function (mermaid) {
      var run = queue.then(function () {
        return mermaid.render('mq-output-diagram-' + (++seq), source);
      });

      queue = run.catch(function () { /* the caller has it */ });
      return run;
    }).then(function (result) {
      remember(source, result.svg);
      return result.svg;
    });

    inFlight.set(source, job);

    var settle = function () { inFlight.delete(source); };
    job.then(settle, settle);

    return job;
  }

  window.mqDiagramOutput = { configure: configure, render: render, cached: cached };
}());
