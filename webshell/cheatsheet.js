/*
  Marqora cheatsheet window.

  A cut-down cousin of app.js. The host renders webshell/cheatsheet.md through the same
  Markdig pipeline the preview uses and posts the HTML here, so the two are guaranteed to
  agree about what markdown looks like. This file adds only what the fragment cannot carry
  on its own: diagrams, math, syntax highlighting, and the scroll position the host wants
  back so it can be restored the next time the window opens.

  Messages in  (host -> page): setContent, setTheme, restoreScroll, requestSelection, selectAll
  Messages out (page -> host): ready, scrolled, linkActivated, contextMenu, selectionCopied, log
*/

(function () {
  'use strict';

  // ------------------------------------------------------------------ bridge

  var webview = window.chrome && window.chrome.webview ? window.chrome.webview : null;

  function post(type, payload) {
    if (!webview) { return; }
    try {
      webview.postMessage(JSON.stringify({ type: type, payload: payload || {} }));
    } catch (err) {
      /* The host has gone away; nothing useful to do from here. */
    }
  }

  function report(level, message, detail) {
    post('log', { level: level, message: String(message), detail: detail ? String(detail) : '' });
  }

  window.addEventListener('error', function (e) {
    report('error', e.message, e.error && e.error.stack ? e.error.stack : e.filename + ':' + e.lineno);
  });

  window.addEventListener('unhandledrejection', function (e) {
    var reason = e.reason || {};
    report('error', reason.message || 'Unhandled promise rejection', reason.stack || '');
  });

  // ------------------------------------------------------------------- state

  var els = {
    root: document.documentElement,
    scroller: document.getElementById('scroller'),
    article: document.getElementById('cheatsheet'),
    boot: document.getElementById('boot')
  };

  var theme = 'Dark';

  /*
    The color theme on screen and the one exports are drawn in, as the host sends them: each
    { light: {...}, dark: {...} }, with the ids that say whether they are the same theme. Null
    until the first setTheme, when the diagrams keep mermaid's stock look.
  */
  var colorTheme = null;
  var exportColorTheme = null;
  var colorThemeKey = '';
  var screenThemeId = '';
  var exportThemeId = '';

  /* Set once the content has been rendered and any saved scroll offset restored. */
  var contentReady = false;

  /* Arrives before the content on a cold start, so it is applied after the render. */
  var pendingScrollTop = 0;

  function loadScript(src) {
    return new Promise(function (resolve, reject) {
      var tag = document.createElement('script');
      tag.src = src;
      tag.onload = function () { resolve(); };
      tag.onerror = function () { reject(new Error('Failed to load ' + src)); };
      document.head.appendChild(tag);
    });
  }

  // --------------------------------------------------------------- diagrams

  /*
    Mermaid runs in the same off-screen frame the preview uses; see the comment at the top
    of mermaid-frame.html for why it needs a document of its own. This page has no AMD
    loader, so the frame is not strictly required here, but sharing it means one diagram
    engine with one set of options rather than a second copy that could drift.
  */
  var mermaidReady = null;

  function mermaidOptions() {
    return {
      startOnLoad: false,
      theme: theme === 'Dark' ? 'dark' : 'default',
      securityLevel: 'strict',
      suppressErrorRendering: true,
      fontFamily: getComputedStyle(els.root).getPropertyValue('--mq-font-ui').trim(),
      // Matches the preview: long sequence notes wrap rather than spill out of their box.
      sequence: { wrap: true }
    };
  }

  // The light drawings a print shows in place of the dark ones; see diagram-output.js.
  window.mqDiagramOutput.configure(mermaidOptions);

  function ensureMermaid() {
    if (mermaidReady) { return mermaidReady; }

    mermaidReady = new Promise(function (resolve, reject) {
      var frame = document.createElement('iframe');
      frame.className = 'mq-diagram-frame';
      frame.setAttribute('aria-hidden', 'true');
      frame.setAttribute('tabindex', '-1');
      frame.setAttribute('title', 'Diagram sandbox');

      var attempts = 0;

      frame.onload = function () {
        // The frame's module script runs after load fires, so wait for it to publish.
        (function waitForModule() {
          var win = frame.contentWindow;

          if (win && win.mermaidReady && win.mermaid) {
            win.mermaid.initialize(mermaidOptions());
            resolve(win.mermaid);
            return;
          }

          if (win && win.mermaidError) {
            reject(new Error('Diagram sandbox: ' + win.mermaidError));
            return;
          }

          if (++attempts > 200) {
            reject(new Error('The diagram sandbox did not finish loading.'));
            return;
          }

          setTimeout(waitForModule, 25);
        }());
      };

      frame.onerror = function () {
        reject(new Error('The diagram sandbox could not be loaded.'));
      };

      frame.src = 'mermaid-frame.html';
      document.body.appendChild(frame);
    });

    return mermaidReady;
  }

  var diagramSeq = 0;

  /// The definition the screen draws: the color theme's diagram colors, in the window's mode.
  function screenDefinition(source) {
    var dark = theme === 'Dark';
    var palette = colorTheme ? (dark ? colorTheme.dark : colorTheme.light) : null;

    return window.mqColorTheme.themedDefinition(source, palette, dark ? 'dark' : 'light');
  }

  /// The definition a print draws: the export theme's light colors, because paper is white.
  function outputDefinition(source) {
    return window.mqColorTheme.themedDefinition(
      source, exportColorTheme ? exportColorTheme.light : null, 'light');
  }

  /// True when a print cannot show the drawing on screen: the window is dark, or exports are
  /// drawn in a theme of their own. The same test as needsOutputCopy in app.js.
  function needsOutputCopy() {
    return theme === 'Dark' || exportThemeId !== screenThemeId;
  }

  /*
    Every diagram drawing in flight, and every light drawing for print, so a print can wait
    for the lot (requestPrintHtml). It did not wait before: a cheatsheet printed while a light
    drawing was still being made went out with the screen's drawing in its place
    (Architecture.md, Screen and output). Errors are caught where the work is made, so this
    only ever settles.
  */
  var outputWork = Promise.resolve();

  function track(work) {
    outputWork = Promise.all([outputWork, work]);
    return work;
  }

  /* The page's content, drawn: set by setContent, null before it arrives. */
  var contentShown = null;

  /// Resolves once the content is in and every drawing it set off is done - or after a bound,
  /// because a print in the screen's colors beats a print that never happens.
  function whenOutputReady() {
    var done = (contentShown || Promise.resolve()).then(function () { return outputWork; });
    var bound = new Promise(function (resolve) { setTimeout(resolve, 10000); });

    return Promise.race([done, bound]);
  }

  /// The light drawings in place of the screen's, in a copy for print: as outputMarkup in
  /// app.js, which a print and every export from the preview go through.
  function outputMarkup(root) {
    Array.prototype.forEach.call(root.querySelectorAll('.mq-diagram-output'), function (copy) {
      var light = copy.querySelector('svg');
      var screen = copy.parentNode ? copy.parentNode.querySelector(':scope > svg') : null;

      if (light && screen) { screen.replaceWith(light); }

      copy.remove();
    });

    return root;
  }

  function renderDiagrams() {
    var nodes = els.article.querySelectorAll('pre.mermaid:not([data-processed])');
    if (nodes.length === 0) { return Promise.resolve(); }

    return track(ensureMermaid().then(function (mermaid) {
      var chain = Promise.resolve();

      // One at a time: mermaid keeps a single working area per document.
      for (var i = 0; i < nodes.length; i++) {
        chain = chain.then(renderOneLater(mermaid, nodes[i]));
      }

      return chain;
    }).catch(function (err) {
      report('warning', 'Mermaid failed to load', err && err.message);
    }));
  }

  function renderOneLater(mermaid, node) {
    return function () {
      var source = node.getAttribute('data-mermaid-source') || node.textContent;
      var id = 'mq-cheatsheet-diagram-' + (++diagramSeq);

      return mermaid.render(id, screenDefinition(source)).then(function (result) {
        node.innerHTML = result.svg;
        node.setAttribute('data-processed', 'true');
        node.setAttribute('data-drawing', id);

        // Not waited on here: the next diagram need not sit behind this one's light drawing.
        // A print waits on it instead, through outputWork.
        track(attachOutputCopy(node, source, id));
      }).catch(function (err) {
        var message = document.createElement('span');
        message.className = 'mq-mermaid-error';
        message.textContent = 'Diagram error: ' + ((err && err.message) || 'could not be parsed');
        node.textContent = '';
        node.appendChild(message);
        node.setAttribute('data-processed', 'true');
      });
    };
  }

  /*
    While the page is dark, each diagram also carries its light drawing, which app.css prints
    in place of the dark one: dark mode is a screen setting, and paper is white. In light mode
    too when exports are drawn in a theme of their own, since the drawing on screen is then the
    wrong theme for paper. The same arrangement as the preview, built from the same shared
    light mermaid.

    Placed only on the drawing it was asked for: a theme change can redraw the diagram while
    this one is still being drawn, and that newer drawing asks for its own.
  */
  function attachOutputCopy(node, source, drawing) {
    if (!needsOutputCopy()) { return Promise.resolve(); }

    return window.mqDiagramOutput.render(outputDefinition(source)).then(function (svg) {
      var screen = node.querySelector(':scope > svg');
      if (!screen || node.getAttribute('data-drawing') !== drawing || !needsOutputCopy()) { return; }

      var holder = document.createElement('div');
      holder.className = 'mq-diagram-output';
      holder.setAttribute('aria-hidden', 'true');
      holder.innerHTML = svg;
      screen.after(holder);
    }).catch(function (err) {
      report('warning', 'A diagram could not be drawn for printing', err && err.message);
    });
  }

  /*
    A theme change has to redraw the diagrams: mermaid bakes its palette into the SVG it
    produces, so restyling the page would leave them the color of the previous theme. Light
    and dark, the color theme and the export theme all come here. Each diagram keeps its old
    drawing until the new one replaces it, so nothing turns back into text on the way.
  */
  function redrawDiagrams() {
    if (!mermaidReady) { return; }

    mermaidReady = mermaidReady.then(function (mermaid) {
      mermaid.initialize(mermaidOptions());
      return mermaid;
    });

    var nodes = els.article.querySelectorAll('pre.mermaid[data-processed]');

    for (var i = 0; i < nodes.length; i++) {
      nodes[i].removeAttribute('data-processed');
    }

    renderDiagrams();
  }

  // ------------------------------------------------------------------ math

  var katexReady = null;

  function ensureKatex() {
    if (katexReady) { return katexReady; }

    katexReady = loadScript('vendor/katex/katex.min.js')
      .then(function () { return loadScript('vendor/katex/auto-render.min.js'); })
      .then(function () { return window.renderMathInElement; });

    return katexReady;
  }

  function renderMath() {
    if (els.article.querySelector('.math') === null) { return Promise.resolve(); }

    return ensureKatex().then(function (renderMathInElement) {
      renderMathInElement(els.article, {
        delimiters: [
          { left: '\\[', right: '\\]', display: true },
          { left: '\\(', right: '\\)', display: false }
        ],
        throwOnError: false,
        errorColor: 'var(--mq-danger)'
      });

      // A light \colorbox shaded for dark mode, as in the preview - see color-theme.js.
      window.mqColorTheme.shadeMathBackgrounds(els.article);
    }).catch(function (err) {
      report('warning', 'KaTeX failed to load', err && err.message);
    });
  }

  // ------------------------------------------------------------ highlighting

  // Token colors are the color theme's syntax slots - see syntax.css and app.js.
  var highlightReady = null;

  function ensureHighlighter() {
    if (highlightReady) { return highlightReady; }

    highlightReady = loadScript('vendor/highlight/highlight.min.js')
      .then(function () { return window.hljs; });

    return highlightReady;
  }

  function highlightCode() {
    var blocks = els.article.querySelectorAll('pre > code[class*="language-"]');
    if (blocks.length === 0) { return Promise.resolve(); }

    return ensureHighlighter().then(function (hljs) {
      if (!hljs) { return; }

      for (var i = 0; i < blocks.length; i++) {
        highlightOne(hljs, blocks[i]);
      }
    }).catch(function (err) {
      report('warning', 'Syntax highlighting is unavailable', err && err.message);
    });
  }

  function highlightOne(hljs, block) {
    var match = /language-([\w#+.-]+)/.exec(block.className);
    if (!match) { return; }

    var language = match[1].toLowerCase();

    // The syntax samples are tagged `text` on purpose: they are markdown shown literally,
    // and coloring them as any language would misrepresent what the reader has to type.
    if (language === 'text' || !hljs.getLanguage(language)) { return; }

    try {
      var result = hljs.highlight(block.textContent, { language: language, ignoreIllegals: true });
      block.innerHTML = result.value;
      block.classList.add('hljs');
    } catch (err) {
      /* Leave the block as plain text. */
    }
  }

  // ------------------------------------------------------------- decoration

  /*
    Distinguishes the two kinds of block the document contains: a `text` fence is markup
    the reader is meant to copy, and what follows it is the result. Marking them here
    rather than with raw HTML in the markdown keeps cheatsheet.md a plain document that
    still reads correctly in any other viewer.
  */
  function decorate() {
    var samples = els.article.querySelectorAll('pre > code.language-text');

    for (var i = 0; i < samples.length; i++) {
      samples[i].parentElement.classList.add('mq-sample');
    }

    // Keep each diagram's definition: mermaid replaces the node's text with an SVG, and a
    // later theme change has to render it again from the original source.
    var diagrams = els.article.querySelectorAll('pre.mermaid');

    for (var d = 0; d < diagrams.length; d++) {
      diagrams[d].setAttribute('data-mermaid-source', diagrams[d].textContent);
    }

    var paragraphs = els.article.querySelectorAll('p');

    for (var p = 0; p < paragraphs.length; p++) {
      if (paragraphs[p].textContent.indexOf('Jump to:') === 0) {
        paragraphs[p].classList.add('mq-jumplist');
        break;
      }
    }

    wrapWideTables();
  }

  function wrapWideTables() {
    var tables = els.article.querySelectorAll('table');

    for (var i = 0; i < tables.length; i++) {
      var table = tables[i];
      if (table.parentElement && table.parentElement.classList.contains('mq-table-scroll')) { continue; }

      var wrapper = document.createElement('div');
      wrapper.className = 'mq-table-scroll';
      table.parentElement.insertBefore(wrapper, table);
      wrapper.appendChild(table);
    }
  }

  // ------------------------------------------------------------------ links

  els.article.addEventListener('click', function (e) {
    var anchor = e.target.closest ? e.target.closest('a[href]') : null;
    if (!anchor) { return; }

    e.preventDefault();
    var href = anchor.getAttribute('href');

    if (href.charAt(0) === '#') {
      var target = els.article.querySelector('[id="' + CSS.escape(href.slice(1)) + '"]');
      if (target) { target.scrollIntoView({ behavior: 'smooth', block: 'start' }); }
      return;
    }

    // Anything leaving the document is the host's decision, so it opens in the browser
    // rather than replacing this page.
    post('linkActivated', { url: href });
  });

  // ----------------------------------------------------------------- scroll

  /*
    The host persists the offset, so it is reported once the user settles rather than on
    every scroll event: a single wheel gesture fires dozens of those, and each one would
    queue a settings write.
  */
  var scrollTimer = null;

  els.scroller.addEventListener('scroll', function () {
    if (!contentReady) { return; }

    if (scrollTimer) { clearTimeout(scrollTimer); }

    scrollTimer = setTimeout(function () {
      scrollTimer = null;
      post('scrolled', { top: Math.round(els.scroller.scrollTop) });
    }, 250);
  }, { passive: true });

  function applyScroll(top) {
    var limit = Math.max(0, els.scroller.scrollHeight - els.scroller.clientHeight);
    els.scroller.scrollTop = Math.min(Math.max(0, top), limit);
  }

  // ---------------------------------------------------------------- inbound

  var handlers = {
    setContent: function (p) {
      els.article.innerHTML = p.html || '';
      decorate();

      contentShown = Promise.all([renderDiagrams(), renderMath(), highlightCode()]).then(function () {
        // Only now is the layout final, so this is the first moment at which a saved
        // offset lands where it did when it was recorded.
        applyScroll(pendingScrollTop);
        contentReady = true;
        els.boot.classList.add('is-hidden');
      });
    },

    setTheme: function (p) {
      /*
        Marqora's teal, before the guard below rather than after it.

        app.css names no accent - it is chosen once, in AccentColors in the app project,
        because the window around the preview paints the same teal with WinUI brushes - so
        this page has nothing to draw a link or a note callout with until the host sends it.
        The first setTheme usually agrees with the theme the page booted in, and would return
        here having set nothing at all.

        --mq-accent-print is the light shade, which the print rules in app.css use whatever
        the window is wearing: this window prints too, and paper is white.
      */
      if (p.accent) {
        els.root.style.setProperty('--mq-accent-screen', p.accent);
      }

      if (p.accentPrint) {
        els.root.style.setProperty('--mq-accent-print', p.accentPrint);
      }

      /*
        The colors the page is drawn in: the color theme the user chose, so the examples look
        the way their own documents do, and the export theme's light palette for print. Every
        setTheme brings both; asking for the pair already showing costs a comparison.
      */
      var redraw = false;

      if (p.colorTheme && p.colorThemeId) {
        var key = p.colorThemeId + '|' + (p.exportColorThemeId || p.colorThemeId);

        if (key !== colorThemeKey) {
          colorTheme = p.colorTheme;
          exportColorTheme = p.exportColorTheme || p.colorTheme;
          screenThemeId = p.colorThemeId;
          exportThemeId = p.exportColorThemeId || p.colorThemeId;
          colorThemeKey = key;

          window.mqColorTheme.apply(colorTheme, exportColorTheme, 'cheatsheet|' + key);
          redraw = true;
        }
      }

      var next = p.theme === 'Dark' ? 'Dark' : 'Light';

      if (next !== theme) {
        theme = next;
        els.root.setAttribute('data-theme', theme === 'Dark' ? 'dark' : 'light');
        redraw = true;
      }

      if (redraw) { redrawDiagrams(); }
    },

    /*
      The cheatsheet for the paged engine, in the shape the preview's requestPrintHtml gives:
      its markup with the light drawings, and the color theme it is drawn in. No paper spec of
      its own; the host supplies it.
    */
    requestPrintHtml: function () {
      whenOutputReady().then(function () {
        var themeSheet = document.getElementById('mq-theme');

        post('printHtml', {
          data: JSON.stringify({
            html: outputMarkup(els.article.cloneNode(true)).innerHTML,
            themeCss: themeSheet ? themeSheet.textContent : '',
            paperCss: '',
            kind: 'cheatsheet',
            rootStyle: document.documentElement.getAttribute('style') || ''
          })
        });
      });
    },

    restoreScroll: function (p) {
      pendingScrollTop = p.top || 0;

      if (contentReady) { applyScroll(pendingScrollTop); }
    },

    /*
      The clipboard is the host's to write. A browser only honours a copy during a trusted
      user gesture, and a click on a native menu is not one, so the page hands the text
      over and the host puts it on the clipboard itself.
    */
    requestSelection: function () {
      var dom = window.getSelection();
      post('selectionCopied', { text: dom && !dom.isCollapsed ? dom.toString() : '' });
    },

    selectAll: function () {
      var range = document.createRange();
      range.selectNodeContents(els.article);

      var dom = window.getSelection();
      if (!dom) { return; }

      dom.removeAllRanges();
      dom.addRange(range);
    }
  };

  /*
    Right-click, reported so the host can put a native menu up. Chromium's own is switched
    off in CheatsheetWindow: it was drawn by Edge and so followed Edge's dark mode rather
    than the app's theme, and it offered browser commands this page has no use for.
  */
  /*
    Ctrl+P is Marqora's Print, not Chromium's. The browser's own shortcuts are left on in this
    window for find and reload, and Ctrl+P was one of them: it opened Chromium's print preview,
    which prints the date, the page title and https://marqora.assets/cheatsheet.html around
    every page and goes round the paged engine entirely. Stopped here, which the browser
    honors for print, and handed to the host's Print.
  */
  window.addEventListener('keydown', function (e) {
    if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && (e.key === 'p' || e.key === 'P')) {
      e.preventDefault();
      post('print', {});
    }
  }, true);

  window.addEventListener('contextmenu', function (e) {
    e.preventDefault();

    var dom = window.getSelection();

    post('contextMenu', {
      x: Math.round(e.clientX),
      y: Math.round(e.clientY),
      hasSelection: !!dom && !dom.isCollapsed
    });
  });

  if (webview) {
    webview.addEventListener('message', function (e) {
      var message = e.data;

      if (typeof message === 'string') {
        try { message = JSON.parse(message); } catch (err) { return; }
      }

      var handler = handlers[message.type];
      if (!handler) { return; }

      try {
        handler(message.payload || {});
      } catch (err) {
        report('error', 'Handler ' + message.type + ' failed: ' + err.message, err.stack);
      }
    });
  }

  post('ready', {});
}());
