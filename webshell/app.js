/*
  Marqora preview shell.

  Both panes live in one WebView so scroll synchronization is a local calculation rather
  than a round trip through the host. The host owns state and markdown rendering; this
  file owns presentation and input, and the two talk over a small JSON message bridge.

  Every message is { type, payload }. Inbound ones name a handler in the `handlers` map
  near the foot of this file and outbound ones go through post(); those two are the list,
  and an inventory repeated up here only ever drifts out of date. A handful expect a reply:
  the request* messages each come back as one post of their own.

  Neither pane draws its own context menu. Monaco's and Chromium's are both switched off,
  and a right-click is reported to the host, which shows a WinUI flyout instead - one menu
  toolkit for the whole app, and one file deciding how every menu in it looks.
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

  // Script errors inside a WebView are invisible otherwise, so forward them to Serilog.
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
    root: document.getElementById('root'),
    sourcePane: document.getElementById('source-pane'),
    previewPane: document.getElementById('preview-pane'),
    preview: document.getElementById('preview'),
    splitter: document.getElementById('splitter'),
    monacoHost: document.getElementById('monaco-host'),
    zoomBadge: document.getElementById('zoom-badge'),
    boot: document.getElementById('boot')
  };

  /*
    The size the source pane is at 100% zoom, before a preference has arrived.

    Zoom multiplies this rather than replacing it, so the two stay independent: changing the
    preferred size does not lose the zoom the user is on, and zooming does not overwrite the
    preference. The live value is state.sourceFontBase; this is only what it starts at, and
    it matches TypographyDefaults.SourceFontSize on the host side.
  */
  var SOURCE_BASE_FONT_PX = 14;
  var ZOOM_STEPS = [50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300, 350, 400, 450, 500];

  /* Marks the spans numberHeadings adds, so a re-number can find and remove its own work. */
  var HEADING_NUMBER_CLASS = 'mq-heading-number';

  /*
    The font stacks app.css ships with, read once at startup.

    Read here rather than restated on the host side, so that the stylesheet stays the one
    place a default font is written down. Captured now because a preference will overwrite
    these very custom properties later, and after that getComputedStyle would report the
    user's choice rather than the default it replaced.

    The host shows these next to the "(default)" entry in the font pickers, which otherwise
    asks the user to choose something without saying what they already have.
  */
  var DEFAULT_FONTS = (function () {
    var root = getComputedStyle(document.documentElement);

    return {
      mono: root.getPropertyValue('--mq-font-mono').trim(),
      ui: root.getPropertyValue('--mq-font-ui').trim()
    };
  }());

  var state = {
    editor: null,
    monaco: null,
    /*
      Read back off the document rather than assumed. The head script has already set
      data-theme from the address the host navigated to, so this agrees with what is on
      screen from the first line - which is what gets Monaco created in the right theme
      (monacoThemeName) instead of built dark and re-themed once setTheme lands.
    */
    theme: document.documentElement.getAttribute('data-theme') === 'dark' ? 'Dark' : 'Light',
    viewMode: 'SideBySide',
    scrollSync: true,
    sourceZoom: 100,
    previewZoom: 100,
    documentBaseUrl: '',
    lineMap: [],
    lineMapDirty: true,
    suppressEditorEvents: false,
    lastHtml: null,
    wordWrap: true,
    showWrapGlyph: false,
    sourceFontBase: SOURCE_BASE_FONT_PX,
    continueLists: true,

    /* 0 for off, otherwise the heading level that counts 1, 2, 3. See numberHeadings. */
    headingNumberStart: 0,

    /*
      One entry per open tab, keyed by the host's document id:

        model            a Monaco ITextModel, which carries that tab's undo history
        viewState        cursor, selection and scroll, captured when the tab is left
        html             the last rendered preview, so returning to a tab is instant
        previewScrollTop where the preview was, restored on return

      Keeping a model per tab rather than calling setValue is what makes undo, cursor and
      scroll survive a tab switch.
    */
    tabs: {},
    activeTabId: null,

    /*
      A Find command asked for while only the preview was showing, held until the source
      pane is back on screen. The widget cannot open on a pane that is display:none, so the
      command waits for the setViewMode the host sends back. See runFindCommand.
    */
    pendingFind: null,

    /*
      Whether the outline panel is open and so wants to be told where the preview is.

      Off until the host asks. Scrolling is continuous and the panel is usually closed, so
      reporting unconditionally would put a message on the bridge for every frame of every
      scroll in exchange for nothing. See reportViewportLine.
    */
    outlineTracking: false,

    /* The last line reported, so an unchanged answer is not sent twice. */
    reportedViewportLine: -1
  };

  // Which pane initiated the current programmatic scroll, so the other side's scroll
  // handler can ignore the echo instead of fighting it.
  var syncOwner = null;
  var syncClearHandle = 0;

  function beginSync(owner) {
    syncOwner = owner;
    if (syncClearHandle) { cancelAnimationFrame(syncClearHandle); }
    syncClearHandle = requestAnimationFrame(function () {
      syncClearHandle = requestAnimationFrame(function () {
        syncOwner = null;
        syncClearHandle = 0;
      });
    });
  }

  function clamp(value, min, max) {
    return value < min ? min : (value > max ? max : value);
  }

  /*
    Temporary probe for the blank-document report. The page is hosted in a WebView that is
    collapsed until a document exists, so it first lays out at zero size; this records
    whether anything is still zero once content has arrived.
  */
  function reportLayout(tag) {
    try {
      var model = state.editor && state.editor.getModel();

      report('info', 'layout[' + tag + ']'
        + ' win=' + window.innerWidth + 'x' + window.innerHeight
        + ' root=' + els.root.clientWidth + 'x' + els.root.clientHeight
        + ' view=' + state.viewMode
        + ' src=' + els.sourcePane.clientWidth + 'x' + els.sourcePane.clientHeight
        + ' host=' + els.monacoHost.clientWidth + 'x' + els.monacoHost.clientHeight
        + ' prev=' + els.previewPane.clientWidth + 'x' + els.previewPane.clientHeight
        + ' editor=' + (state.editor ? 'yes' : 'no')
        + ' lines=' + (model ? model.getLineCount() : -1)
        + ' dom=' + els.preview.innerHTML.length
        + ' boot=' + (els.boot.classList.contains('is-hidden') ? 'hidden' : 'SHOWING'));
    } catch (err) {
      report('warning', 'Layout probe failed', err && err.message);
    }
  }

  // The WebView is resized by the host when the document surface becomes visible. Monaco
  // must be told, because it caches its dimensions and will not re-measure on its own.
  var resizeProbe = 0;

  window.addEventListener('resize', function () {
    if (state.editor) { state.editor.layout(); }
    state.lineMapDirty = true;

    if (resizeProbe) { clearTimeout(resizeProbe); }
    resizeProbe = setTimeout(function () { reportLayout('resize'); }, 250);
  });

  // ------------------------------------------------------------------- zoom

  function stepZoom(current, direction) {
    if (direction > 0) {
      for (var i = 0; i < ZOOM_STEPS.length; i++) {
        if (ZOOM_STEPS[i] > current) { return ZOOM_STEPS[i]; }
      }
      return ZOOM_STEPS[ZOOM_STEPS.length - 1];
    }
    for (var j = ZOOM_STEPS.length - 1; j >= 0; j--) {
      if (ZOOM_STEPS[j] < current) { return ZOOM_STEPS[j]; }
    }
    return ZOOM_STEPS[0];
  }

  var badgeHandle = 0;

  function flashZoomBadge(paneLabel, percent) {
    els.zoomBadge.textContent = paneLabel + ' ' + percent + '%';
    els.zoomBadge.classList.add('is-visible');

    if (badgeHandle) { clearTimeout(badgeHandle); }
    badgeHandle = setTimeout(function () {
      els.zoomBadge.classList.remove('is-visible');
      badgeHandle = 0;
    }, 1100);
  }

  function applySourceZoom(percent, announce) {
    state.sourceZoom = percent;
    if (state.editor) {
      state.editor.updateOptions({ fontSize: state.sourceFontBase * (percent / 100) });
    }
    if (announce) { flashZoomBadge('Source', percent); }
  }

  function applyPreviewZoom(percent, announce) {
    state.previewZoom = percent;
    document.documentElement.style.setProperty('--mq-preview-scale', String(percent / 100));
    state.lineMapDirty = true;
    if (announce) { flashZoomBadge('Preview', percent); }
  }

  /*
    Which family a font stack will actually be drawn in.

    A CSS stack is a list of wishes: the browser takes the first one the machine has. Nothing
    in the DOM reports which that was - getComputedStyle hands back the stack it was given,
    not the face that won - so the only way to know is to measure.

    Rendering the same string in "Family, monospace" and in bare "monospace" gives the same
    width when Family is missing, because both fall back to the same face. Three generics are
    tried because a real font can happen to match one of them; matching all three would be a
    coincidence too far.

    This is what lets the preferences dialog say "Using Consolas" when someone typed a font
    they do not have, rather than leaving them to wonder why nothing changed.
  */
  function fontAvailable(family) {
    if (GENERIC_FAMILIES.indexOf(family.toLowerCase()) !== -1) { return true; }

    var probe = 'mmmmmmmmmmlliWWWij0O';
    var canvas = fontAvailable.canvas || (fontAvailable.canvas = document.createElement('canvas'));
    var ctx = canvas.getContext('2d');

    for (var i = 0; i < FONT_BASELINES.length; i++) {
      var generic = FONT_BASELINES[i];

      ctx.font = '72px ' + generic;
      var fallbackWidth = ctx.measureText(probe).width;

      ctx.font = '72px "' + family + '", ' + generic;

      if (ctx.measureText(probe).width !== fallbackWidth) { return true; }
    }

    return false;
  }

  var GENERIC_FAMILIES = ['monospace', 'sans-serif', 'serif', 'system-ui', 'cursive', 'fantasy'];
  var FONT_BASELINES = ['monospace', 'sans-serif', 'serif'];

  /* The first family in a stack that this machine can actually draw. */
  function resolveFont(stack) {
    var families = String(stack || '').split(',');

    for (var i = 0; i < families.length; i++) {
      var family = families[i].trim().replace(/^["']|["']$/g, '');

      if (family && fontAvailable(family)) { return family; }
    }

    return '';
  }

  /*
    Tells the host which fonts the panes ended up in.

    Sent on startup and again after any preference change, because a change is exactly when
    the answer can move - and because the dialog that asks the question is open at the time.
  */
  function reportResolvedFonts() {
    var root = getComputedStyle(document.documentElement);

    post('fontsResolved', {
      sourceFont: resolveFont(root.getPropertyValue('--mq-font-mono')),
      previewFont: resolveFont(root.getPropertyValue('--mq-font-ui'))
    });
  }

  /*
    Writes a font-family custom property, or clears it when no font was chosen.

    Clearing rather than writing '' matters: app.css declares a stack of four faces so that a
    machine without Cascadia Code still gets something monospaced, and an empty declaration
    would shadow that stack rather than fall back to it.
  */
  function setFontProperty(root, name, family) {
    if (family) {
      root.style.setProperty(name, family);
    } else {
      root.style.removeProperty(name);
    }
  }

  /*
    Numbers the preview's headings, or strips the numbers when the preference is off.

    Written into the DOM rather than drawn with CSS counters, which is what this started as.
    Counters cannot cope with a document that skips a level - and skipping is normal, not
    exotic: a '###' sitting directly under a '#' with no '##' between them is everywhere in
    real notes. A counter chain has to render the missing level as something, so those
    headings came out as "9.0.1", and the level above the numbered range could not reset the
    levels below it at all, so the deeper numbers ran on across sections instead of starting
    again.

    Doing it here fixes both, and pays for itself twice over: the numbers are real text, so
    they travel into the HTML export, the PDF, the printed page and the rich-text clipboard
    without any of them needing to know this feature exists. Word gets numbered headings too,
    which the CSS version could never have managed.

    The markdown source is still never touched. This runs on the rendered copy only.
  */
  function numberHeadings() {
    var previous = els.preview.querySelectorAll('.' + HEADING_NUMBER_CLASS);
    for (var p = 0; p < previous.length; p++) {
      previous[p].remove();
    }

    var start = state.headingNumberStart;
    if (!start) { return; }

    // One counter per level, so a heading only ever has to look at its own and its parents'.
    var counters = [0, 0, 0, 0, 0, 0];
    var headings = els.preview.querySelectorAll('h1, h2, h3, h4, h5, h6');

    for (var i = 0; i < headings.length; i++) {
      var heading = headings[i];
      var level = parseInt(heading.tagName.charAt(1), 10);

      if (level < start) {
        /*
          Above the numbered range: left unnumbered, but it still begins a new section, so
          everything below it starts again. This is the half CSS counters could not express,
          and the reason numbering ran on across a document's chapters.
        */
        for (var r = start - 1; r < 6; r++) { counters[r] = 0; }
        continue;
      }

      counters[level - 1]++;

      for (var d = level; d < 6; d++) { counters[d] = 0; }

      var parts = [];
      for (var c = start - 1; c < level; c++) {
        // A level the document skipped is still zero here. Dropping those leading zeros is
        // what turns "0.1" into "1" for a heading whose parent level was never used.
        if (counters[c] === 0 && parts.length === 0) { continue; }

        parts.push(counters[c]);
      }

      if (parts.length === 0) { continue; }

      var label = document.createElement('span');
      label.className = HEADING_NUMBER_CLASS;
      label.textContent = parts.join('.') + '  ';

      heading.insertBefore(label, heading.firstChild);
    }
  }

  function changeZoom(pane, direction) {
    var current = pane === 'Source' ? state.sourceZoom : state.previewZoom;
    var next = direction === 0 ? 100 : stepZoom(current, direction);

    if (next === current) { return; }

    if (pane === 'Source') { applySourceZoom(next, true); } else { applyPreviewZoom(next, true); }
    post('zoomChanged', { pane: pane, percent: next });
  }

  /// Steps both panes together, each from its own current value.
  function changeZoomBoth(direction) {
    var source = direction === 0 ? 100 : stepZoom(state.sourceZoom, direction);
    var preview = direction === 0 ? 100 : stepZoom(state.previewZoom, direction);

    if (source === state.sourceZoom && preview === state.previewZoom) { return; }

    applySourceZoom(source, false);
    applyPreviewZoom(preview, false);
    flashZoomBadge('Both panes', preview);

    post('zoomChanged', { pane: 'Source', percent: source });
    post('zoomChanged', { pane: 'Preview', percent: preview });
  }

  /*
    Right-click in either pane, reported to the host so it can put a native menu up.

    Neither pane draws a menu of its own any more - Monaco's is off and so is Chromium's -
    so this is the only thing standing between a right-click and nothing happening at all.

    What was under the pointer goes with the message: the host builds the menu from it and
    never asks the page anything afterwards, which would race with the next keystroke.
    Coordinates are viewport pixels, which is what the host wants, and neither pane's zoom
    disturbs them: the source pane zooms by font size and the preview by scaling its own
    content, so the pointer stays where it was relative to the WebView.
  */
  function paneHasSelection(pane) {
    if (pane === 'Source') {
      var selection = state.editor && state.editor.getSelection();
      return !!selection && !selection.isEmpty();
    }

    var dom = window.getSelection();
    if (!dom || dom.isCollapsed || dom.rangeCount === 0) { return false; }

    // A selection made in the source pane is not the preview's to copy.
    return els.preview.contains(dom.getRangeAt(0).commonAncestorContainer);
  }

  function absoluteUrl(value) {
    if (!value) { return ''; }
    return isAbsolute(value) ? value : (state.documentBaseUrl + value.replace(/^\.\//, ''));
  }

  function wirePaneContextMenus() {
    wireContextMenu(els.sourcePane, 'Source');
    wireContextMenu(els.previewPane, 'Preview');

    function wireContextMenu(element, pane) {
      element.addEventListener('contextmenu', function (e) {
        // Chromium is already told not to draw a menu, but a page that leaves the default
        // action in place also leaves a caret-moving side effect behind on some elements.
        e.preventDefault();

        var anchor = e.target.closest ? e.target.closest('a[href]') : null;
        var image = e.target.closest ? e.target.closest('img[src]') : null;
        var href = anchor ? anchor.getAttribute('href') : '';

        var spelling = spellingAt(pane, e.clientX, e.clientY);

        post('contextMenu', {
          pane: pane,
          x: Math.round(e.clientX),
          y: Math.round(e.clientY),
          hasSelection: paneHasSelection(pane),
          // A link to a heading in this same document is not worth a Copy Link item:
          // there is no address to paste anywhere.
          linkUrl: href.charAt(0) === '#' ? '' : absoluteUrl(href),
          imageUrl: image ? absoluteUrl(image.getAttribute('src')) : '',

          // Empty when the pointer was not over a misspelling, which is how the host decides
          // whether to offer suggestions at all. Zero-based, like every other position the
          // host is given.
          word: spelling ? spelling.word : '',
          wordLine: spelling ? spelling.line : -1,
          wordStart: spelling ? spelling.start : -1,
          wordEnd: spelling ? spelling.end : -1,
          wordRepeated: !!(spelling && spelling.repeated)
        });
      });
    }
  }

  /*
    The misspelling under the pointer, or null.

    Asks the marker rather than the model's own getWordAtPosition. The marker is the range the
    analyzer produced, so the word offered for replacement is exactly the one that was flagged -
    there is no chance of Monaco's word separators disagreeing with the masker about where a
    word begins, which they would for a hyphenated or apostrophised one.

    Only the source pane has markers; the preview is HTML.
  */
  function spellingAt(pane, clientX, clientY) {
    if (pane !== 'Source' || !state.editor || !state.monaco) { return null; }

    var model = state.editor.getModel();
    if (!model) { return null; }

    var target = state.editor.getTargetAtClientPoint(clientX, clientY);
    if (!target || !target.position) { return null; }

    return spellingAtPosition(model, target.position);
  }

  /*
    The misspelling covering one position, or null.

    Shared by the two ways of asking: a right-click, which has a point on screen, and Ctrl+.,
    which has the caret. Both end up here so both offer exactly the same word.
  */
  function spellingAtPosition(model, position) {
    // An empty range at the position: what comes back is every decoration covering that spot.
    var covering = model.getDecorationsInRange(new state.monaco.Range(
      position.lineNumber, position.column, position.lineNumber, position.column));

    for (var i = 0; i < covering.length; i++) {
      var options = covering[i].options || {};
      var className = options.inlineClassName;

      // Ours, and not the selection ink or anything Monaco put there itself.
      if (className !== 'mq-misspelled' && className !== 'mq-repeated-word') { continue; }

      var range = covering[i].range;

      return {
        word: model.getValueInRange(range),
        line: range.startLineNumber - 1,
        start: range.startColumn - 1,
        end: range.endColumn - 1,
        // Which class it wears is what tells a repeated word from a misspelling: one is
        // replaced, the other is deleted.
        repeated: className === 'mq-repeated-word'
      };
    }

    return null;
  }

  /*
    Ctrl+. - the corrections, without the mouse.

    It raises the same WinUI menu a right-click does, at the caret instead of at the pointer,
    rather than going through Monaco's own quick-fix list. One menu, one set of items, one place
    that decides how they look - the same reason both panes report their right-click to the host
    instead of drawing a menu of their own. It also keeps the lightbulb and the "no quick fixes"
    line out of the editor, neither of which was wanted.

    Silent when the caret is not on a misspelling. There is nothing to offer, and a menu saying
    so is the sort of box that has to be dismissed for no reason.
  */
  function openSpellingMenuAtCaret() {
    var editor = state.editor;
    var model = editor && editor.getModel();

    if (!model || !state.monaco) { return; }

    var position = editor.getPosition();

    if (!position) { return; }

    var spelling = spellingAtPosition(model, position);

    if (!spelling) { return; }

    // Where that character actually is on screen. The menu wants the WebView's own coordinates,
    // which is what a pointer event would have given it, so the editor's own offset goes back on.
    var visible = editor.getScrolledVisiblePosition(position);

    if (!visible) { return; }

    var host = els.monacoHost.getBoundingClientRect();

    post('contextMenu', {
      pane: 'Source',
      x: Math.round(host.left + visible.left),

      // Below the line rather than on it, so the menu does not cover the word it is about.
      y: Math.round(host.top + visible.top + visible.height),
      hasSelection: paneHasSelection('Source'),
      linkUrl: '',
      imageUrl: '',
      word: spelling.word,
      wordLine: spelling.line,
      wordStart: spelling.start,
      wordEnd: spelling.end,
      wordRepeated: spelling.repeated
    });
  }

  function wireCtrlWheelZoom(element, pane) {
    element.addEventListener('wheel', function (e) {
      if (!e.ctrlKey) { return; }

      e.preventDefault();
      e.stopPropagation();

      var direction = e.deltaY < 0 ? 1 : -1;

      // Ctrl+Shift+wheel moves both panes, so a side-by-side reading stays balanced.
      if (e.shiftKey) { changeZoomBoth(direction); } else { changeZoom(pane, direction); }
    }, { passive: false, capture: true });
  }

  // --------------------------------------------------------------- line map

  // Maps zero-based markdown line -> vertical offset inside the preview scroller.
  // Only the outermost element for a given line is kept, and the list is strictly
  // increasing in both line and offset so a binary search stays valid.
  function buildLineMap() {
    var nodes = els.preview.querySelectorAll('[data-src-line]');
    var map = [];
    var lastLine = -1;
    var lastTop = -1;

    for (var i = 0; i < nodes.length; i++) {
      var node = nodes[i];
      var line = parseInt(node.getAttribute('data-src-line'), 10);

      if (!isFinite(line) || line <= lastLine) { continue; }

      var top = node.offsetTop;
      if (top < lastTop) { continue; }

      map.push({ line: line, top: top });
      lastLine = line;
      lastTop = top;
    }

    state.lineMap = map;
    state.lineMapDirty = false;
    return map;
  }

  function lineMap() {
    if (state.lineMapDirty || state.lineMap.length === 0) { return buildLineMap(); }
    return state.lineMap;
  }

  function interpolate(map, key, fromKey, toKey) {
    if (map.length === 0) { return 0; }
    if (key <= map[0][fromKey]) { return map[0][toKey]; }

    var last = map[map.length - 1];
    if (key >= last[fromKey]) { return last[toKey]; }

    var lo = 0;
    var hi = map.length - 1;

    while (lo < hi - 1) {
      var mid = (lo + hi) >> 1;
      if (map[mid][fromKey] <= key) { lo = mid; } else { hi = mid; }
    }

    var a = map[lo];
    var b = map[hi];
    var span = b[fromKey] - a[fromKey];
    var t = span > 0 ? (key - a[fromKey]) / span : 0;

    return a[toKey] + t * (b[toKey] - a[toKey]);
  }

  // ------------------------------------------------------------ scroll sync

  // Fractional zero-based line currently at the top of the editor viewport.
  function editorTopLine() {
    var editor = state.editor;
    if (!editor) { return 0; }

    var model = editor.getModel();
    if (!model) { return 0; }

    var scrollTop = editor.getScrollTop();
    var lineCount = model.getLineCount();
    var lo = 1;
    var hi = lineCount;

    while (lo < hi) {
      var mid = (lo + hi + 1) >> 1;
      if (editor.getTopForLineNumber(mid) <= scrollTop) { lo = mid; } else { hi = mid - 1; }
    }

    var top = editor.getTopForLineNumber(lo);
    var nextTop = lo < lineCount ? editor.getTopForLineNumber(lo + 1) : top + 1;
    var fraction = nextTop > top ? (scrollTop - top) / (nextTop - top) : 0;

    return (lo - 1) + clamp(fraction, 0, 1);
  }

  function scrollPreviewToLine(line) {
    var map = lineMap();
    if (map.length === 0) { return; }

    var target = interpolate(map, line, 'line', 'top');
    var max = els.previewPane.scrollHeight - els.previewPane.clientHeight;

    els.previewPane.scrollTop = clamp(target, 0, Math.max(0, max));
  }

  function previewTopLine() {
    var map = lineMap();
    if (map.length === 0) { return 0; }
    return interpolate(map, els.previewPane.scrollTop, 'top', 'line');
  }

  function syncEditorToPreview() {
    if (!state.scrollSync || state.viewMode !== 'SideBySide') { return; }
    beginSync('source');
    scrollPreviewToLine(editorTopLine());
  }

  function syncPreviewToEditor() {
    if (!state.scrollSync || state.viewMode !== 'SideBySide' || !state.editor) { return; }

    // Between closing the last tab and opening the next there is no model at all.
    var model = state.editor.getModel();
    if (!model) { return; }

    beginSync('preview');

    var lineCount = model.getLineCount();
    var line = previewTopLine();
    var lineNumber = clamp(Math.floor(line) + 1, 1, lineCount);
    var top = state.editor.getTopForLineNumber(lineNumber);
    var next = state.editor.getTopForLineNumber(Math.min(lineNumber + 1, lineCount));
    var fraction = line - Math.floor(line);

    state.editor.setScrollTop(top + (next - top) * fraction);
  }

  /*
    Tells the host which source line the preview is showing, for the outline panel to
    highlight.

    Coalesced onto an animation frame rather than debounced. A scroll produces events far
    faster than the panel can usefully repaint, but unlike a search or a re-render there is
    nothing expensive at the far end - so the answer should keep up with the scroll rather
    than arrive a beat after it stops, which is what a debounce would give.

    Whole lines only, and only when the answer moves. previewTopLine is fractional and
    changes on every pixel; the outline cares which heading the line falls under, and that
    cannot change without the integer changing.
  */
  var viewportReportHandle = 0;

  function reportViewportLine() {
    if (!state.outlineTracking) { return; }

    if (viewportReportHandle) { return; }

    viewportReportHandle = requestAnimationFrame(function () {
      viewportReportHandle = 0;

      if (!state.outlineTracking) { return; }

      var line = Math.max(0, Math.floor(previewTopLine()));

      if (line === state.reportedViewportLine) { return; }

      state.reportedViewportLine = line;
      post('viewportLine', { line: line });
    });
  }

  els.previewPane.addEventListener('scroll', function () {
    reportViewportLine();

    if (syncOwner === 'source') { return; }
    syncPreviewToEditor();
  }, { passive: true });

  /*
    The map is built once the markdown, diagrams and maths are in, but the preview keeps
    changing height after that: an image decodes, a font arrives, a diagram is re-themed.
    Everything below the change moves and the map does not know, so from then on the panes
    disagree by exactly that much, and stay that way until something else rebuilds it.

    Watching the article's size catches every such change at once. The editor is the
    stable side, so the preview is put back under its top line rather than the other way
    round; the scroll that causes fires as owned by the source and is ignored by the
    preview's own handler, so this cannot feed back on itself.
  */
  if (typeof ResizeObserver === 'function') {
    new ResizeObserver(function () {
      state.lineMapDirty = true;
      if (syncOwner !== 'preview') { syncEditorToPreview(); }
    }).observe(els.preview);
  }

  // ------------------------------------------------------- preview pipeline

  var lazyScripts = {};

  function loadScript(src) {
    if (lazyScripts[src]) { return lazyScripts[src]; }

    lazyScripts[src] = new Promise(function (resolve, reject) {
      var tag = document.createElement('script');
      tag.src = src;
      tag.onload = function () { resolve(); };
      tag.onerror = function () { reject(new Error('Failed to load ' + src)); };
      document.head.appendChild(tag);
    });

    return lazyScripts[src];
  }

  /*
    Monaco installs a global AMD `define` and marks it with `define.amd`. UMD bundles check
    that marker and register themselves as anonymous AMD modules, which Monaco's loader
    rejects: "Can only have one anonymous define call per script file". KaTeX and
    highlight.js both hit this.

    Monaco reads the marker only once, when deciding whether to install itself, and
    registers every module of its own by name. Dropping the marker afterwards therefore
    costs Monaco nothing and lets the other bundles take their ordinary browser path.

    Mermaid needs more than this and is dealt with separately; see mermaid-frame.html.
  */
  function releaseAmdMarker() {
    if (typeof window.define === 'function' && window.define.amd) {
      delete window.define.amd;
    }
  }

  // --- mermaid -------------------------------------------------------------

  var mermaidReady = null;
  var mermaidCache = {};
  var mermaidSeq = 0;

  function mermaidTheme() {
    return state.theme === 'Dark' ? 'dark' : 'default';
  }

  function mermaidOptions() {
    return {
      startOnLoad: false,
      theme: mermaidTheme(),
      // Documents come from the user's own disk, but the preview still refuses
      // arbitrary HTML and click handlers inside diagram labels.
      securityLevel: 'strict',
      suppressErrorRendering: true,
      fontFamily: getComputedStyle(document.documentElement).getPropertyValue('--mq-font-ui').trim()
    };
  }

  /*
    Mermaid lives in an off-screen same-origin frame with no module loader in it. See the
    comment at the top of mermaid-frame.html for why. The frame is created on first use, so
    a document without diagrams never pays for it.
  */
  /// Diagnostic detail for when the sandbox does not come up, which is otherwise opaque.
  function describeFrame(frame) {
    try {
      var win = frame.contentWindow;
      var doc = frame.contentDocument;

      return '[href=' + (win && win.location ? win.location.href : 'none')
        + ' readyState=' + (doc ? doc.readyState : 'none')
        + ' scripts=' + (doc ? doc.scripts.length : 'none')
        + ' title=' + (doc ? doc.title : 'none') + ']';
    } catch (err) {
      return '[inaccessible: ' + err.message + ']';
    }
  }

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

          // The frame records its own failures, since its errors never reach this window.
          if (win && win.mermaidError) {
            reject(new Error('Diagram sandbox: ' + win.mermaidError));
            return;
          }

          if (++attempts > 200) {
            reject(new Error('The diagram sandbox did not finish loading. ' + describeFrame(frame)));
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

  /// Re-initializes an already-loaded mermaid after a theme change.
  function reinitializeMermaid() {
    if (!mermaidReady) { return; }

    mermaidReady = mermaidReady.then(function (mermaid) {
      mermaid.initialize(mermaidOptions());
      return mermaid;
    });
  }

  /*
    Diagrams a pop-out window is currently showing, as "documentId:index" strings, and the
    SVG each was last sent. The host sets the list; nothing is reported for a diagram nobody
    is looking at, which is almost always all of them.
  */
  /*
    Diagrams with a pop-out window open, keyed by the window's own id.

    Each entry follows one diagram through the document by its definition rather than by
    where it sits. Position is not identity: delete the diagram above yours and the window
    would otherwise be handed its neighbour, silently and looking like an ordinary update.

    Content is not identity either, since it changes on every keystroke, so the two work
    together. The definition finds the diagram while it is unchanged. When it cannot be
    found, the number of diagrams in the document says which thing happened: an unchanged
    count means the definition was edited where it stood, and a smaller one means a block
    was deleted - and since ours is the one that cannot be found, it was ours.
  */
  var watchedDiagrams = {};

  /// Diagram count per document as of its last render: the edit-or-delete signal above.
  var diagramCounts = {};

  function setWatchedDiagrams(items) {
    var next = {};

    for (var i = 0; i < items.length; i++) {
      var item = items[i];

      // An entry already following a diagram keeps what it has tracked to. Only a newly
      // opened window starts from the definition the host opened it on.
      next[item.id] = watchedDiagrams[item.id] || {
        id: item.id,
        documentId: item.documentId,
        hash: item.hash,
        index: -1,
        sent: null,
        gone: false,
        invalid: null
      };
    }

    watchedDiagrams = next;
  }

  /*
    Position is a diagram's identity, so every rendered diagram is numbered by where it sits
    in the document. Stamped over all of them rather than only the ones about to render:
    a cached diagram keeps its node but its ordinal can still have moved.
  */
  function numberDiagrams() {
    var all = els.preview.querySelectorAll('pre.mermaid');

    for (var i = 0; i < all.length; i++) {
      all[i].setAttribute('data-mq-index', i);
    }

    return all;
  }

  /// Every diagram in the preview, in document order, with its definition and its markup.
  function currentDiagrams() {
    var blocks = els.preview.querySelectorAll('pre.mermaid');
    var list = [];

    for (var i = 0; i < blocks.length; i++) {
      var block = blocks[i];
      var rendered = block.hasAttribute('data-mq-diagram') ? block.querySelector('svg') : null;

      // A block that failed to parse carries mermaid's own complaint instead of an SVG, and
      // that message is worth passing on rather than reducing to "something is wrong".
      var failure = rendered ? null : block.querySelector('.mq-mermaid-error');

      list.push({
        hash: block.getAttribute('data-mq-diagram'),
        svg: rendered ? rendered.outerHTML : null,
        error: failure ? failure.textContent : null
      });
    }

    return list;
  }

  /*
    Position of a diagram with this definition, preferring the one nearest to where the
    window last saw it.

    Two byte-identical diagrams in one document are genuinely indistinguishable, so nearest
    is the best answer available: it keeps a window on the copy it was already following
    rather than jumping to whichever comes first in the document.
  */
  function findDiagram(diagrams, hash, near) {
    var best = -1;

    if (!hash) { return best; }

    for (var i = 0; i < diagrams.length; i++) {
      if (diagrams[i].hash !== hash) { continue; }
      if (best < 0 || Math.abs(i - near) < Math.abs(best - near)) { best = i; }
    }

    return best;
  }

  /// Reports watched diagrams that changed, and any that are no longer in the document.
  function reportDiagrams() {
    if (!state.activeTabId) { return; }

    var diagrams = currentDiagrams();
    var before = diagramCounts[state.activeTabId];

    for (var id in watchedDiagrams) {
      var watched = watchedDiagrams[id];

      // Diagrams in other documents are not on screen to be compared against. They are not
      // gone, merely out of view, and reporting them would be a lie.
      if (watched.documentId !== state.activeTabId) { continue; }

      var at = findDiagram(diagrams, watched.hash, watched.index);

      // Nothing matches the definition, but no block was added or removed: it was edited
      // where it stood, so whatever is still at that position is the same diagram.
      if (at < 0
          && before === diagrams.length
          && watched.index >= 0
          && watched.index < diagrams.length) {
        at = watched.index;
      }

      if (at < 0) {
        // A block went, and the one we cannot account for is this one.
        if (!watched.gone) {
          watched.gone = true;

          // Forgetting what was last sent matters as much as the report itself. Undo brings
          // the diagram back with byte-identical markup, and without this the update that
          // clears the notice would be suppressed as a no-op - leaving the window insisting
          // its source is gone while showing the diagram that is back.
          watched.sent = null;

          post('diagramRemoved', { id: watched.id });
        }

        continue;
      }

      var found = diagrams[at];

      if (!found.svg) {
        // Mermaid said why it could not parse this. The window keeps its last good render -
        // that is the thing you are looking at while fixing the mistake - and says that what
        // it is showing no longer matches the source.
        //
        // Nothing is reported while the block simply has not rendered yet, which is the
        // ordinary state halfway through a keystroke.
        if (found.error && watched.invalid !== found.error) {
          watched.invalid = found.error;

          // As with removal: a corrected diagram often renders byte-identically to the last
          // good one, and the update that clears this notice must not be dropped as a no-op.
          watched.sent = null;

          post('diagramInvalid', { id: watched.id, message: found.error });
        }

        continue;
      }

      watched.gone = false;
      watched.invalid = null;
      watched.index = at;

      if (watched.sent === found.svg) { continue; }

      watched.hash = found.hash;
      watched.sent = found.svg;

      post('diagramUpdated', { id: watched.id, hash: found.hash, index: at, svg: found.svg });
    }

    diagramCounts[state.activeTabId] = diagrams.length;
  }

  /// A closed document takes its diagrams with it, and reopening it makes new ones.
  function reportDiagramsGone(documentId) {
    for (var id in watchedDiagrams) {
      var watched = watchedDiagrams[id];

      if (watched.documentId !== documentId || watched.gone) { continue; }

      watched.gone = true;
      watched.sent = null;

      post('diagramRemoved', { id: watched.id });
    }

    delete diagramCounts[documentId];
  }

  function renderDiagrams() {
    var nodes = els.preview.querySelectorAll('pre.mermaid:not([data-processed])');

    if (nodes.length === 0) {
      // Still renumber: an edit elsewhere in the document can move a cached diagram.
      numberDiagrams();
      reportDiagrams();
      return Promise.resolve();
    }

    // Rendered one at a time: mermaid keeps a single working area per document, so
    // concurrent renders in the same frame interfere with each other.
    return ensureMermaid().then(function (mermaid) {
      var chain = Promise.resolve();

      for (var i = 0; i < nodes.length; i++) {
        chain = chain.then(renderOneDiagramLater(mermaid, nodes[i]));
      }

      return chain;
    }).then(function () {
      numberDiagrams();
      reportDiagrams();
    }).catch(function (err) {
      report('warning', 'Mermaid failed to load', err && err.message);
    });
  }

  function renderOneDiagramLater(mermaid, node) {
    return function () { return renderOneDiagram(mermaid, node); };
  }

  /*
    Identifies a diagram by its definition, for the pop-out windows.

    Rendering replaces the node's text with SVG, so the definition is gone by the time
    anyone double-clicks; this is stamped on the node while it is still available. Hashing
    rather than storing the source keeps the attribute short, and makes the same diagram in
    two tabs resolve to the same window, which is the behaviour one expects.

    djb2. A hash collision would only ever raise the wrong pop-out.
  */
  function diagramKey(source) {
    var hash = 5381;

    for (var i = 0; i < source.length; i++) {
      hash = (((hash << 5) + hash) + source.charCodeAt(i)) | 0;
    }

    return (hash >>> 0).toString(36);
  }

  function renderOneDiagram(mermaid, node) {
    var source = node.textContent;
    var cached = mermaidCache[source];
    var key = diagramKey(source);

    // Re-rendering an unchanged diagram on every keystroke is the single biggest
    // cost in a document full of diagrams, so cache the SVG by its definition.
    if (cached) {
      node.innerHTML = cached;
      node.setAttribute('data-processed', 'true');
      node.setAttribute('data-mq-diagram', key);
      state.lineMapDirty = true;
      return Promise.resolve();
    }

    return mermaid.render('mq-diagram-' + (++mermaidSeq), source).then(function (result) {
      mermaidCache[source] = result.svg;
      node.innerHTML = result.svg;
      node.setAttribute('data-processed', 'true');
      node.setAttribute('data-mq-diagram', key);
      state.lineMapDirty = true;
    }).catch(function (err) {
      var message = document.createElement('span');
      message.className = 'mq-mermaid-error';
      message.textContent = 'Diagram error: ' + ((err && err.message) || 'could not be parsed');
      node.textContent = '';
      node.appendChild(message);
      node.setAttribute('data-processed', 'true');
      state.lineMapDirty = true;
    });
  }

  // --- maths ---------------------------------------------------------------

  var katexReady = null;

  // With the AMD marker gone, both files take their browser path and publish globals.
  function ensureKatex() {
    if (katexReady) { return katexReady; }

    katexReady = loadScript('vendor/katex/katex.min.js')
      .then(function () { return loadScript('vendor/katex/auto-render.min.js'); })
      .then(function () { return window.renderMathInElement; });

    return katexReady;
  }

  function renderMath() {
    if (els.preview.querySelector('.math') === null) { return Promise.resolve(); }

    return ensureKatex().then(function (renderMathInElement) {
      renderMathInElement(els.preview, {
        delimiters: [
          { left: '\\[', right: '\\]', display: true },
          { left: '\\(', right: '\\)', display: false }
        ],
        throwOnError: false,
        errorColor: 'var(--mq-danger)'
      });
      state.lineMapDirty = true;
    }).catch(function (err) {
      report('warning', 'KaTeX failed to load', err && err.message);
    });
  }

  // --- code highlighting ---------------------------------------------------

  /*
    Preview code blocks are highlighted by highlight.js rather than by Monaco's colorizer.

    Monaco can colorize, but it fetches each language grammar on demand through the AMD
    loader, and those fetches would collide with the window in which mermaid needs `define`
    to be absent. highlight.js is a single self-contained bundle with no loader involvement,
    which removes the interleaving entirely: after start-up Monaco loads nothing further, so
    hiding `define` for mermaid is safe by construction.
  */

  var highlightReady = null;

  /// highlight.js ships a stylesheet per theme, so the pair is swapped rather than restyled.
  function applyHighlightTheme() {
    var dark = state.theme === 'Dark';
    var light = document.getElementById('hljs-light');
    var night = document.getElementById('hljs-dark');

    if (light) { light.disabled = dark; }
    if (night) { night.disabled = !dark; }
  }

  function ensureHighlighter() {
    if (highlightReady) { return highlightReady; }

    highlightReady = loadScript('vendor/highlight/highlight.min.js')
      .then(function () { return window.hljs; });

    return highlightReady;
  }

  function highlightCode() {
    var blocks = els.preview.querySelectorAll('pre > code[class*="language-"]');
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

    // An unregistered language would throw; plain text is the right fallback.
    if (!hljs.getLanguage(language)) { return; }

    try {
      var result = hljs.highlight(block.textContent, { language: language, ignoreIllegals: true });
      block.innerHTML = result.value;
      block.classList.add('hljs');
    } catch (err) {
      /* Leave the block as plain text. */
    }
  }

  // --- link and asset rewriting -------------------------------------------

  function isAbsolute(url) {
    return /^[a-z][a-z0-9+.-]*:/i.test(url) || url.indexOf('//') === 0;
  }

  // Relative paths in a document resolve against the file's own folder, which the
  // host maps to the marqora.document virtual origin.
  function rewriteRelativeUrls() {
    if (!state.documentBaseUrl) { return; }

    var media = els.preview.querySelectorAll('img[src], video[src], source[src], audio[src]');

    for (var i = 0; i < media.length; i++) {
      var src = media[i].getAttribute('src');
      if (src && !isAbsolute(src) && src.charAt(0) !== '#') {
        media[i].setAttribute('src', state.documentBaseUrl + src.replace(/^\.\//, ''));
      }
    }
  }

  function wrapWideTables() {
    var tables = els.preview.querySelectorAll('table');

    for (var i = 0; i < tables.length; i++) {
      var table = tables[i];
      if (table.parentElement && table.parentElement.classList.contains('mq-table-scroll')) { continue; }

      var wrapper = document.createElement('div');
      wrapper.className = 'mq-table-scroll';
      table.parentElement.insertBefore(wrapper, table);
      wrapper.appendChild(table);
    }
  }

  /*
    Double-clicking a diagram opens it in its own window.

    The rendered SVG goes across rather than the definition, so the new window needs neither
    mermaid nor a render pass and cannot disagree with what is on screen. The key travels
    with it so the host can raise an existing window instead of opening a second one.
  */
  els.preview.addEventListener('dblclick', function (e) {
    var diagram = e.target.closest ? e.target.closest('pre.mermaid[data-mq-diagram]') : null;
    if (!diagram) { return; }

    var svg = diagram.querySelector('svg');
    if (!svg) { return; }

    // Otherwise the double-click also selects the text under the pointer, leaving the
    // preview with a highlighted run once the new window takes focus.
    e.preventDefault();
    var selection = window.getSelection();
    if (selection) { selection.removeAllRanges(); }

    post('diagramActivated', {
      documentId: state.activeTabId,
      index: Number(diagram.getAttribute('data-mq-index')),
      hash: diagram.getAttribute('data-mq-diagram'),
      svg: svg.outerHTML
    });
  });

  els.preview.addEventListener('click', function (e) {
    var anchor = e.target.closest ? e.target.closest('a[href]') : null;
    if (!anchor) { return; }

    e.preventDefault();
    var href = anchor.getAttribute('href');

    if (href.charAt(0) === '#') {
      var target = els.preview.querySelector('[id="' + CSS.escape(href.slice(1)) + '"]');
      if (target) { target.scrollIntoView({ behavior: 'smooth', block: 'start' }); }
      return;
    }

    // Everything else is the host's decision, so it can open the default browser
    // or load a sibling markdown file in place.
    post('linkActivated', { url: isAbsolute(href) ? href : (state.documentBaseUrl + href) });
  });

  // --- applying a render ---------------------------------------------------

  function applyPreviewHtml(html, resetScroll) {
    if (html === state.lastHtml) { return Promise.resolve(); }

    // Anchor on the source line at the top of the viewport so the preview does not
    // jump while the user is typing further down the document.
    var anchorLine = resetScroll ? 0 : previewTopLine();

    state.lastHtml = html;
    els.preview.innerHTML = html;
    state.lineMapDirty = true;

    wrapWideTables();
    rewriteRelativeUrls();
    numberHeadings();

    if (resetScroll) {
      els.previewPane.scrollTop = 0;
    }

    return Promise.all([renderDiagrams(), renderMath(), highlightCode()]).then(function () {
      buildLineMap();

      if (resetScroll) {
        els.previewPane.scrollTop = 0;
        return;
      }

      if (state.scrollSync && state.viewMode === 'SideBySide') {
        syncEditorToPreview();
      } else {
        beginSync('source');
        scrollPreviewToLine(anchorLine);
      }
    });
  }

  // ------------------------------------------------------ active block cue

  var activeBlock = null;

  function highlightActiveBlock(zeroBasedLine) {
    if (state.viewMode !== 'SideBySide') { return; }

    var map = lineMap();
    if (map.length === 0) { return; }

    var chosen = null;
    for (var i = 0; i < map.length; i++) {
      if (map[i].line <= zeroBasedLine) { chosen = map[i]; } else { break; }
    }

    if (!chosen) { return; }

    var element = els.preview.querySelector('[data-src-line="' + chosen.line + '"]');
    if (element === activeBlock) { return; }

    if (activeBlock) { activeBlock.classList.remove('mq-active-block'); }
    if (element) { element.classList.add('mq-active-block'); }
    activeBlock = element;
  }

  /// Set by wireSplitter below, and called when the host asks for an even split.
  var splitterReset = null;

  // --------------------------------------------------------------- splitter

  (function wireSplitter() {
    var dragging = false;

    function fractionFromClientX(clientX) {
      var bounds = els.root.getBoundingClientRect();
      if (bounds.width === 0) { return 0.5; }
      return clamp((clientX - bounds.left) / bounds.width, 0.15, 0.85);
    }

    function apply(fraction) {
      document.documentElement.style.setProperty('--mq-split', String(fraction));
      state.lineMapDirty = true;
      if (state.editor) { state.editor.layout(); }
    }

    function resetSplit() {
      dragging = false;
      els.splitter.classList.remove('is-dragging');
      apply(0.5);
      post('splitterMoved', { position: 0.5 });
    }

    /*
      Double-click is detected from the pointer stream rather than from a dblclick handler.

      preventDefault below stops the browser starting a text selection as the splitter is
      dragged, which it must - but that same call suppresses the compatibility mouse events,
      and dblclick is one of them. A dblclick listener here would simply never run.
    */
    var lastDownAt = 0;
    var lastDownX = 0;

    els.splitter.addEventListener('pointerdown', function (e) {
      var isSecondClick = (e.timeStamp - lastDownAt) < 400 && Math.abs(e.clientX - lastDownX) < 6;

      lastDownAt = e.timeStamp;
      lastDownX = e.clientX;

      if (isSecondClick) {
        // Not a third click's opener.
        lastDownAt = 0;
        resetSplit();
        e.preventDefault();
        return;
      }

      dragging = true;
      els.splitter.setPointerCapture(e.pointerId);
      els.splitter.classList.add('is-dragging');
      e.preventDefault();
    });

    els.splitter.addEventListener('pointermove', function (e) {
      if (!dragging) { return; }
      apply(fractionFromClientX(e.clientX));
    });

    function endDrag(e) {
      if (!dragging) { return; }
      dragging = false;
      els.splitter.classList.remove('is-dragging');

      if (e.pointerId !== undefined && els.splitter.hasPointerCapture(e.pointerId)) {
        els.splitter.releasePointerCapture(e.pointerId);
      }

      var fraction = parseFloat(
        getComputedStyle(document.documentElement).getPropertyValue('--mq-split')) || 0.5;

      post('splitterMoved', { position: fraction });
    }

    els.splitter.addEventListener('pointerup', endDrag);
    els.splitter.addEventListener('pointercancel', endDrag);

    // Kept as a second route in case a future input path does deliver dblclick. Harmless
    // when it never fires, and resetting an already-even split is a no-op.
    els.splitter.addEventListener('dblclick', function (e) {
      e.preventDefault();
      resetSplit();
    });

    // Published for the host, which asks for a reset when the toolbar's Split button is
    // double-clicked. The handler table is defined further down the file, so this cannot
    // register itself there directly.
    splitterReset = resetSplit;

    // Keyboard resize, so the splitter is reachable without a mouse.
    els.splitter.addEventListener('keydown', function (e) {
      var current = parseFloat(
        getComputedStyle(document.documentElement).getPropertyValue('--mq-split')) || 0.5;
      var delta = 0;

      if (e.key === 'ArrowLeft') { delta = -0.02; }
      else if (e.key === 'ArrowRight') { delta = 0.02; }
      else if (e.key === 'Home') { delta = 0.5 - current; }
      else { return; }

      e.preventDefault();
      var next = clamp(current + delta, 0.15, 0.85);
      apply(next);
      post('splitterMoved', { position: next });
    });
  }());

  // ------------------------------------------------------------ wrap glyphs

  /*
    Monaco has no option for marking where a wrapped line continues, so the markers are
    drawn as an overlay.

    Everything needed is public API: getTopForLineNumber gives the pixel top of a model
    line, and the gap to the following line divided by the line height is the number of
    visual rows that line occupies. A marker is placed at the right edge of every row but
    the last. Only the visible range is measured, so the cost does not grow with the file.
  */

  var wrapLayer = null;

  function ensureWrapLayer() {
    if (!wrapLayer) {
      wrapLayer = document.createElement('div');
      wrapLayer.className = 'mq-wrap-layer';
      wrapLayer.setAttribute('aria-hidden', 'true');
      els.monacoHost.appendChild(wrapLayer);
    }

    return wrapLayer;
  }

  function updateWrapGlyphs() {
    var editor = state.editor;
    if (!editor || !state.monaco) { return; }

    var layer = ensureWrapLayer();

    if (!state.showWrapGlyph || !state.wordWrap) {
      if (layer.firstChild) { layer.textContent = ''; }
      return;
    }

    var model = editor.getModel();
    if (!model) { return; }

    var lineHeight = editor.getOption(state.monaco.editor.EditorOption.lineHeight);
    var layout = editor.getLayoutInfo();
    var scrollTop = editor.getScrollTop();
    var lineCount = model.getLineCount();
    var ranges = editor.getVisibleRanges();
    var markers = [];

    for (var r = 0; r < ranges.length; r++) {
      var from = ranges[r].startLineNumber;
      var to = ranges[r].endLineNumber;

      for (var line = from; line <= to; line++) {
        var top = editor.getTopForLineNumber(line);
        var bottom = line < lineCount ? editor.getTopForLineNumber(line + 1) : top + lineHeight;
        var rows = Math.round((bottom - top) / lineHeight);

        // A single-row line has nothing to mark.
        for (var row = 0; row < rows - 1; row++) {
          markers.push(Math.round(top - scrollTop + (row * lineHeight)));
        }
      }
    }

    paintWrapMarkers(layer, markers, lineHeight, layout);
  }

  function paintWrapMarkers(layer, tops, lineHeight, layout) {
    // Reuse the existing nodes: this runs on every scroll frame.
    while (layer.childElementCount > tops.length) {
      layer.removeChild(layer.lastChild);
    }

    while (layer.childElementCount < tops.length) {
      var glyph = document.createElement('span');
      glyph.className = 'mq-wrap-glyph';
      layer.appendChild(glyph);
    }

    var right = Math.max(0, layout.width - layout.contentLeft - layout.verticalScrollbarWidth);

    for (var i = 0; i < tops.length; i++) {
      var node = layer.children[i];
      node.style.top = tops[i] + 'px';
      node.style.left = (layout.contentLeft + right - 14) + 'px';
      node.style.height = lineHeight + 'px';
      node.style.lineHeight = lineHeight + 'px';
    }
  }

  // ----------------------------------------------------------------- monaco

  function defineThemes(monaco) {
    var style = getComputedStyle(document.documentElement);

    function token(name) { return style.getPropertyValue(name).trim(); }

    /*
      The same colour, quieter: an alpha byte on the end of a plain #rrggbb.

      A colour that already carries alpha, or is not hex at all, is handed back untouched -
      Monaco reads these with Color.fromHex and paints anything malformed bright red, and a
      derived colour is not worth that. Derived rather than sent as a third value, so the
      host's MatchColors stays the one place a colour is chosen.
    */
    function soften(hex, alpha) {
      return /^#[0-9a-fA-F]{6}$/.test(hex) ? hex + alpha : hex;
    }

    monaco.editor.defineTheme('marqora-light', {
      base: 'vs',
      inherit: true,
      rules: [],
      colors: {
        'editor.background': token('--mq-bg'),
        'editor.foreground': token('--mq-text'),
        'editorLineNumber.foreground': token('--mq-text-tertiary'),
        'editorLineNumber.activeForeground': token('--mq-accent'),
        'editorGutter.background': token('--mq-bg'),
        'editor.lineHighlightBackground': token('--mq-bg-subtle'),
        'editorIndentGuide.background1': token('--mq-border'),
        // Monaco's default whitespace marks are nearly invisible; a tinted colour makes
        // Show Whitespace actually readable without competing with the text.
        'editorWhitespace.foreground': token('--mq-whitespace'),

        /*
          The spelling red, and the one place it is chosen.

          Misspellings are drawn as decorations rather than markers, so this key no longer
          colours a squiggle - the stylesheet does that. What it still colours is the tick each
          one puts in the overview ruler, which names this id rather than a hex value so the
          ticks follow a theme change without anything being re-checked. See setSpelling.

          Nothing else in the app publishes an Info marker, so the key is free for this.

          --mq-danger rather than a colour of its own: it is already the app's red and already
          has a dark-mode value, and a second token holding the same colour is a second thing
          to keep in step.
        */
        'editorInfo.foreground': token('--mq-danger'),

        /*
          One colour for both, so a selection does not change colour when the keyboard
          leaves the editor.

          vs gives these two different answers - #ADD6FF focused, #E5EBF1 unfocused - and
          the second is near enough to the page to read as nothing there at all. So text
          that is still selected turns pale the moment focus goes anywhere else: the tab
          strip, a toolbar button, or the Find All window, which is exactly where stepping
          a results list leaves it. That last one is the same complaint the dark pair below
          answers with inactiveSelectionBackground; light was simply never given it.

          The value is vs's own focused colour, so nothing looks different from before -
          the selection just stops dimming. Only the pair Monaco dims is set here; the find
          match colours have no unfocused form and keep vs's.
        */
        'editor.selectionBackground': token('--mq-selection-light'),
        'editor.inactiveSelectionBackground': token('--mq-selection-light')
      }
    });

    var dark = {
      'editor.background': token('--mq-bg'),
      'editor.foreground': token('--mq-text'),
      'editorLineNumber.foreground': token('--mq-text-tertiary'),
      'editorLineNumber.activeForeground': token('--mq-accent'),
      'editorGutter.background': token('--mq-bg'),
      'editor.lineHighlightBackground': token('--mq-bg-subtle'),
      'editorIndentGuide.background1': token('--mq-border'),
      // Monaco's default whitespace marks are nearly invisible; a tinted colour makes
      // Show Whitespace actually readable without competing with the text.
      'editorWhitespace.foreground': token('--mq-whitespace'),

      // The spelling squiggle. See the light theme above for why info means misspelling and
      // why it borrows --mq-danger rather than carrying a colour of its own.
      'editorInfo.foreground': token('--mq-danger')
    };

    /*
      The dark-mode hit colours: one pair, every place a match is pointed at.

      --mq-selection and --mq-selection-text are put on the document by setTheme, from the
      host's MatchColors - the single place either colour is chosen, because the Find All
      window paints the same pair in WinUI. The keys below are all that one pair, because
      they are all the same idea: this is the text you asked for.

        selectionBackground          selected text, and a result picked in Find All
        inactiveSelectionBackground  the same, with the keyboard still in the Find All
                                     window - which is where stepping a results list leaves
                                     it, so without this the highlight arrives already dim
        findMatchBackground          the current match in the source pane's own Find (Ctrl+F)
        findMatchHighlightBackground the other matches Find turned up, softened, so the one
                                     being looked at still stands out from the rest

      Monaco's own vs-dark colours for these are a navy, a grey and an orange: three
      different answers to one question, and the two that matter most are the faintest.

      The foreground goes on the two that are drawn at full strength. The softened one is
      mostly page showing through, so the ordinary text colour is the readable one there.

      Left out when nothing has been posted yet. defineThemes runs at editor creation as
      well, which can be before the first setTheme - and Monaco parses these with
      Color.fromHex, which paints anything it cannot understand bright red. setTheme calls
      this again after it sets them, so the definition is right by the time it counts.
    */
    var selection = token('--mq-selection');
    var selectionText = token('--mq-selection-text');

    if (selection) {
      dark['editor.selectionBackground'] = selection;
      dark['editor.inactiveSelectionBackground'] = selection;
      dark['editor.findMatchBackground'] = selection;
      dark['editor.findMatchHighlightBackground'] = soften(selection, '55');
    }

    if (selectionText) {
      dark['editor.selectionForeground'] = selectionText;
      dark['editor.findMatchForeground'] = selectionText;
    }

    monaco.editor.defineTheme('marqora-dark', {
      base: 'vs-dark',
      inherit: true,
      rules: [],
      colors: dark
    });
  }

  function monacoThemeName() {
    return state.theme === 'Dark' ? 'marqora-dark' : 'marqora-light';
  }

  /*
    The text inside a selection, dark mode only.

    Monaco has a theme colour for exactly this - editor.selectionForeground, set from
    MatchColors alongside the background - and outside a high-contrast theme it does nothing
    at all. The span it colours, .inline-selected-text, is only ever emitted when the theme
    type is high contrast; every other theme leaves selected text in its ordinary token
    colour, which on an opaque selection is near-white on pale blue. Nor is there anything
    for a stylesheet to reach instead: the selection is painted as a layer behind the text
    rather than as a box around it.

    So the foreground is applied the way Monaco colours text everywhere else - an inline
    decoration over each selected range, carrying the class app.css paints from
    --mq-selection-text. The decorations belong to the model rather than to the editor, so
    they follow a tab across a switch instead of needing to be torn down and rebuilt.

    The theme colour is still set: it costs nothing, it is the documented way to say this,
    and it is what a high-contrast Windows theme would read. This is the part that shows.
  */
  function paintSelectionForeground() {
    var editor = state.editor;
    var tab = editor && state.activeTabId ? state.tabs[state.activeTabId] : null;

    if (!tab || !tab.model || editor.getModel() !== tab.model) { return; }

    var ink = [];

    if (state.theme === 'Dark') {
      var selections = editor.getSelections() || [];

      for (var i = 0; i < selections.length; i++) {
        if (!selections[i].isEmpty()) {
          ink.push({
            range: selections[i],
            options: { inlineClassName: 'mq-selected-text', description: 'marqora-selection' }
          });
        }
      }
    }

    tab.selectionInk = tab.model.deltaDecorations(tab.selectionInk || [], ink);
  }

  var emitTextChange = debounce(function () {
    if (!state.editor || !state.activeTabId) { return; }
    post('editorTextChanged', { documentId: state.activeTabId, text: state.editor.getValue() });
  }, 160);

  var emitStats = debounce(function () {
    if (!state.editor) { return; }

    var model = state.editor.getModel();
    if (!model) { return; }

    var position = state.editor.getPosition();
    var text = model.getValue();
    var words = text.match(/[^\s]+/g);

    post('stats', {
      line: position ? position.lineNumber : 1,
      column: position ? position.column : 1,
      lineCount: model.getLineCount(),
      words: words ? words.length : 0,
      characters: text.length
    });
  }, 220);

  /*
    Formatting-toolbar state, on its own debounce rather than riding emitStats.

    emitStats is trailing-edge at 220ms and walks the whole document for its word count, so
    toggles hung off it would sit frozen while the user typed or held an arrow key and only
    settle once they stopped. This reads one small window of lines instead, so it can run
    far more often for far less.
  */
  var CARET_STATE_MAX_LINES = 500;

  /*
    Whether undo or redo has anything to act on.

    Monaco tracks both, but keeps canUndo/canRedo off the published ITextModel surface -
    they exist to drive its own context keys - so this asks the model and shrugs if some
    future build stops answering. The shrug is `true` rather than `false`: a button that
    stays lit does exactly what it did before this was wired up, while one stuck grey
    would take the command away altogether.
  */
  function canHistory(model, method) {
    return typeof model[method] === 'function' ? !!model[method]() : true;
  }

  var emitCaretState = debounce(function () {
    var editor = state.editor;
    var model = editor && editor.getModel();
    var selection = editor && editor.getSelection();

    if (!model || !selection || !state.activeTabId) { return; }

    var first = Math.max(1, selection.startLineNumber - 1);
    var last = Math.min(model.getLineCount(), selection.endLineNumber + 1);

    /*
      Ctrl+A on a large document would otherwise ship the whole thing across the bridge on
      every selection change. Nobody is reading a checkmark for a ten-thousand-line
      selection, so the host is told to report nothing rather than sent the document.
    */
    if (last - first + 1 > CARET_STATE_MAX_LINES) {
      post('caretState', {
        documentId: state.activeTabId,
        truncated: true,
        canUndo: canHistory(model, 'canUndo'),
        canRedo: canHistory(model, 'canRedo')
      });
      return;
    }

    var lines = [];
    for (var i = first; i <= last; i++) { lines.push(model.getLineContent(i)); }

    post('caretState', {
      documentId: state.activeTabId,
      truncated: false,
      canUndo: canHistory(model, 'canUndo'),
      canRedo: canHistory(model, 'canRedo'),
      firstLine: first - 1,
      lines: lines,
      startLine: selection.startLineNumber - 1,
      startColumn: selection.startColumn - 1,
      endLine: selection.endLineNumber - 1,
      endColumn: selection.endColumn - 1
    });
  }, 60);

  function debounce(fn, wait) {
    var handle = 0;
    return function () {
      if (handle) { clearTimeout(handle); }
      handle = setTimeout(function () { handle = 0; fn(); }, wait);
    };
  }

  // -------------------------------------------------------------- shortcuts

  /*
    Which pane a zoom key means.

    In the two single-pane views there is only one answer. In split view the question is
    only ever asked from one of the two halves of a shortcut - Monaco's, which runs when the
    editor has the keyboard, or the window's, which stands down when it does - so the
    editor's own focus is the whole of it.
  */
  function focusedPane() {
    if (state.viewMode === 'Source') { return 'Source'; }
    if (state.viewMode === 'Preview') { return 'Preview'; }

    return state.editor && state.editor.hasWidgetFocus() ? 'Source' : 'Preview';
  }

  function zoomActivePane(direction) {
    return function () { changeZoom(focusedPane(), direction); };
  }

  function zoomBothPanes(direction) {
    return function () { changeZoomBoth(direction); };
  }

  /*
    Every shortcut the host owns, written down once.

    Each of these has to be answered from two places. Monaco claims the keyboard whenever
    the caret is in the source pane, and a XAML accelerator never fires while the WebView
    holds it, so a key known only to the window is dead in the editor and a key known only
    to Monaco is dead everywhere else. Preview view has no editor focused at all: the source
    pane is display:none, which blurs Monaco and drops focus onto the page - which is why
    Ctrl+S did nothing while you were reading.

    So each entry is registered twice from the one declaration: with Monaco, which also puts
    it ahead of any built-in binding on the same key, and on the window for the rest of the
    page. The window half is bubble phase and stands down while the editor has focus, so
    exactly one of the two ever runs.

    `code` is a KeyboardEvent.code, and monaco.KeyCode spells its members the same way, so
    the Monaco keybinding is derived from it rather than written out a second time.

    `run` is either a command name for the host - see OnHostCommand in MainViewModel - or a
    function, for the few the shell answers by itself.

    RegisterAccelerators in MainWindow is the other half of this, for when the chrome rather
    than the WebView holds the keyboard; the two lists say the same thing and change
    together. KeyboardShortcuts.cs is what Help shows.

    Not here: Ctrl+F and its family, which need the source pane brought back before a widget
    can open (see runFindCommand below), and Ctrl+C/X/V/Z/Y/A, which are Monaco's own.
  */
  var HOST_SHORTCUTS = [
    // Files.
    { ctrl: true, code: 'KeyN', run: 'newTab' },
    { ctrl: true, code: 'KeyT', run: 'newTab' },
    { ctrl: true, code: 'KeyO', run: 'open' },
    { ctrl: true, shift: true, code: 'KeyO', run: 'openFolder' },
    { ctrl: true, code: 'KeyS', run: 'save' },
    { ctrl: true, shift: true, code: 'KeyS', run: 'saveAll' },
    { ctrl: true, alt: true, code: 'KeyS', run: 'saveAs' },
    { ctrl: true, code: 'KeyW', run: 'close' },
    { ctrl: true, shift: true, code: 'KeyW', run: 'closeAll' },
    { ctrl: true, code: 'KeyP', run: 'print' },

    /*
      Tabs. Ctrl+1 to Ctrl+8 select that tab and Ctrl+9 jumps to the last, as in every
      browser and as Help has always said.

      Ctrl+1 to Ctrl+3 used to be registered here as the view switches, so with the caret in
      the editor - which is nearly always - they selected a view, and everywhere else they
      selected a tab. The view modes are on Alt+1 to Alt+3 below, where the window has had
      them all along.
    */
    { ctrl: true, code: 'Digit1', run: 'tab.1' },
    { ctrl: true, code: 'Digit2', run: 'tab.2' },
    { ctrl: true, code: 'Digit3', run: 'tab.3' },
    { ctrl: true, code: 'Digit4', run: 'tab.4' },
    { ctrl: true, code: 'Digit5', run: 'tab.5' },
    { ctrl: true, code: 'Digit6', run: 'tab.6' },
    { ctrl: true, code: 'Digit7', run: 'tab.7' },
    { ctrl: true, code: 'Digit8', run: 'tab.8' },
    { ctrl: true, code: 'Digit9', run: 'tab.last' },
    { ctrl: true, code: 'Tab', run: 'nextTab' },
    { ctrl: true, shift: true, code: 'Tab', run: 'previousTab' },

    // View.
    { alt: true, code: 'Digit1', run: 'viewSource' },
    { alt: true, code: 'Digit2', run: 'viewSplit' },
    { alt: true, code: 'Digit3', run: 'viewPreview' },

    /*
      Alt+4 joins the three above because the outline is the other thing the View menu
      shows, though it is a toggle rather than a fourth member of that radio set. Showing
      the panel takes the keyboard into it; Escape there brings it back to the document.

      Alt+Shift+4 is the way into a panel that is already open, and back out again. It has
      to be a key of its own: from here - the caret in the source pane, the panel already
      showing - a visibility toggle could only close the thing being reached for.
    */
    { alt: true, code: 'Digit4', run: 'toggleOutline' },
    { alt: true, shift: true, code: 'Digit4', run: 'focusOutline' },

    { alt: true, code: 'KeyZ', run: 'wordWrap' },

    // Spell check. The window has the same binding; this is the half that fires while the
    // caret is in the editor, which is most of the time.
    { code: 'F7', run: 'toggleSpellCheck' },

    // The corrections for the word at the caret. Answered here rather than by the host: only
    // this side knows where the caret is or what is underlined beneath it.
    { ctrl: true, code: 'Period', run: openSpellingMenuAtCaret },

    /*
      Zoom, answered here rather than by the host: the panes are this file's to scale, and
      the host is told afterwards so the size is remembered. Both the main row and the
      numeric keypad, and Ctrl+Shift for the two panes together, matching Ctrl+Shift+wheel.
    */
    { ctrl: true, code: 'Equal', run: zoomActivePane(1) },
    { ctrl: true, code: 'NumpadAdd', run: zoomActivePane(1) },
    { ctrl: true, code: 'Minus', run: zoomActivePane(-1) },
    { ctrl: true, code: 'NumpadSubtract', run: zoomActivePane(-1) },
    { ctrl: true, code: 'Digit0', run: zoomActivePane(0) },
    { ctrl: true, code: 'Numpad0', run: zoomActivePane(0) },
    { ctrl: true, shift: true, code: 'Equal', run: zoomBothPanes(1) },
    { ctrl: true, shift: true, code: 'NumpadAdd', run: zoomBothPanes(1) },
    { ctrl: true, shift: true, code: 'Minus', run: zoomBothPanes(-1) },
    { ctrl: true, shift: true, code: 'NumpadSubtract', run: zoomBothPanes(-1) },
    { ctrl: true, shift: true, code: 'Digit0', run: zoomBothPanes(0) },
    { ctrl: true, shift: true, code: 'Numpad0', run: zoomBothPanes(0) },

    // Edit and tools.
    { shift: true, alt: true, code: 'KeyF', run: 'formatDocument' },
    { ctrl: true, shift: true, code: 'KeyF', run: 'findAll' },
    { ctrl: true, shift: true, code: 'KeyC', run: 'copyRichText' },
    { ctrl: true, code: 'F1', run: 'cheatsheet' },

    /*
      The Format menu.

      Headings 1 to 6 are menu-only. Ctrl+digit belongs to tab selection, and Ctrl+Alt+digit
      is indistinguishable from AltGr+digit on European layouts, where it types characters.
    */
    { ctrl: true, code: 'KeyB', run: 'md.bold' },
    { ctrl: true, code: 'KeyI', run: 'md.italic' },
    { ctrl: true, code: 'KeyK', run: 'md.link' },
    { ctrl: true, code: 'Backquote', run: 'md.inlineCode' },
    { ctrl: true, shift: true, code: 'KeyX', run: 'md.strikethrough' },
    { ctrl: true, shift: true, code: 'KeyK', run: 'md.codeBlock' },
    { ctrl: true, shift: true, code: 'Period', run: 'md.blockquote' },
    { ctrl: true, shift: true, code: 'Digit8', run: 'md.bulletList' },
    { ctrl: true, shift: true, code: 'Digit7', run: 'md.numberedList' },
    { ctrl: true, shift: true, code: 'BracketRight', run: 'md.headingIncrease' },
    { ctrl: true, shift: true, code: 'BracketLeft', run: 'md.headingDecrease' },

    // Opening the menus. Format takes O because File has F, the way Windows menus have
    // always split those two. Alt on its own is watched separately, further down.
    { alt: true, code: 'KeyF', run: 'menu.file' },
    { alt: true, code: 'KeyE', run: 'menu.edit' },
    { alt: true, code: 'KeyO', run: 'menu.format' },
    { alt: true, code: 'KeyV', run: 'menu.view' },
    { alt: true, code: 'KeyT', run: 'menu.tools' },
    { alt: true, code: 'KeyH', run: 'menu.help' }
  ];

  function runShortcut(entry) {
    if (typeof entry.run === 'function') { entry.run(); } else { post('command', { name: entry.run }); }
  }

  /// Binds the table to both keyboards. Called once, as the editor is created.
  function registerHostShortcuts() {
    var monaco = state.monaco;

    HOST_SHORTCUTS.forEach(function (entry) {
      var binding = monaco.KeyCode[entry.code];

      if (typeof binding !== 'number') {
        report('warning', 'Shortcut key ' + entry.code + ' is not a monaco.KeyCode; skipped.');
        return;
      }

      if (entry.ctrl) { binding |= monaco.KeyMod.CtrlCmd; }
      if (entry.shift) { binding |= monaco.KeyMod.Shift; }
      if (entry.alt) { binding |= monaco.KeyMod.Alt; }

      state.editor.addCommand(binding, function () { runShortcut(entry); });
    });

    /*
      The other half, for wherever in the page the keyboard is when the editor does not
      have it - the preview, the splitter, the article after a click on a heading.

      Bubble phase, and it stands down while the editor holds focus: Monaco has already run
      its own copy of the same entry and stopped the key before it could reach here, and the
      explicit check covers anything that slipped past it.
    */
    window.addEventListener('keydown', function (e) {
      if (e.metaKey) { return; }
      if (state.editor && state.editor.hasWidgetFocus && state.editor.hasWidgetFocus()) { return; }

      for (var i = 0; i < HOST_SHORTCUTS.length; i++) {
        var entry = HOST_SHORTCUTS[i];

        if (e.code !== entry.code) { continue; }
        if (e.ctrlKey !== !!entry.ctrl) { continue; }
        if (e.shiftKey !== !!entry.shift) { continue; }
        if (e.altKey !== !!entry.alt) { continue; }

        e.preventDefault();
        runShortcut(entry);
        return;
      }
    });
  }

  function createEditor(monaco) {
    state.monaco = monaco;
    defineThemes(monaco);

    state.editor = monaco.editor.create(els.monacoHost, {
      // No implicit model: each tab supplies its own, so the editor starts empty.
      model: null,
      theme: monacoThemeName(),
      automaticLayout: true,
      fontFamily: getComputedStyle(document.documentElement).getPropertyValue('--mq-font-mono').trim(),
      fontSize: state.sourceFontBase * (state.sourceZoom / 100),
      lineNumbers: 'on',
      wordWrap: 'on',
      wrappingIndent: 'same',
      minimap: { enabled: false },
      // Off, and not merely because nothing renders it. Sticky scroll is on by default in
      // Monaco, and it charges for itself whether or not it draws anything: the reveal
      // computation moves the target up by stickyScroll.maxLineCount lines - five - before
      // it works out where the top of the viewport should be. Nothing ever draws a sticky
      // header here, because markdown has no document-symbol provider and its only folding
      // is region markers, so those five lines were paid and never used. They are what left
      // a heading clicked in the outline sitting five lines below the top of the pane. See
      // scrollToLine, which asks for the top and now gets it.
      stickyScroll: { enabled: false },
      renderLineHighlight: 'line',
      scrollBeyondLastLine: true,
      smoothScrolling: true,
      cursorSmoothCaretAnimation: 'on',
      cursorBlinking: 'smooth',
      padding: { top: 18, bottom: 18 },
      renderWhitespace: 'selection',
      occurrencesHighlight: 'off',
      selectionHighlight: false,
      // Zoom is a host-level concept here, driven through the bridge so both panes
      // and the toolbar readout agree.
      mouseWheelZoom: false,
      unicodeHighlight: { ambiguousCharacters: false },
      bracketPairColorization: { enabled: false },
      guides: { indentation: false },
      quickSuggestions: false,
      /*
        Let a hover escape the pane it is in.

        Monaco draws hovers as content widgets positioned inside the editor, and .mq-pane is
        overflow:hidden - so a marker hover near the right-hand edge was cut off at the
        splitter, with the rest of it drawn underneath. The splitter is position:relative with
        no z-index and comes after the source pane in the DOM, so it paints over anything the
        pane lets through.

        This switches the overflow-widget container to position:fixed, which is not clipped by
        an ancestor's overflow. It works here because nothing above the editor establishes a
        containing block for fixed elements - no transform, filter, will-change or contain on
        .mq-pane, .mq-root or body. The transforms in app.css are on the zoom badge and the
        boot spinner, neither of which is an ancestor.

        Not specific to spelling: document problems have always had hovers, and they were
        clipped the same way.
      */
      fixedOverflowWidgets: true,
      // Monaco's own context menu is off, and so is Chromium's. Both panes report the
      // right-click to the host instead, which puts a WinUI flyout up: one menu toolkit
      // for the whole app rather than three, and one place that decides how they look.
      // See wirePaneContextMenus below.
      contextmenu: false,
      scrollbar: { verticalScrollbarSize: 12, horizontalScrollbarSize: 12, useShadows: false }
    });

    state.editor.onDidChangeModelContent(function () {
      updateWrapGlyphs();

      if (state.suppressEditorEvents) { return; }
      emitTextChange();
      emitStats();
      emitCaretState();
    });

    state.editor.onDidScrollChange(function () {
      updateWrapGlyphs();

      if (syncOwner === 'preview') { return; }
      syncEditorToPreview();
    });

    state.editor.onDidLayoutChange(updateWrapGlyphs);

    state.editor.onDidChangeCursorPosition(function (e) {
      emitStats();
      highlightActiveBlock(e.position.lineNumber - 1);
    });

    // Selection rather than position: this also fires when a selection grows or shrinks
    // without the caret itself landing anywhere new, which changes what the toggles mean.
    state.editor.onDidChangeCursorSelection(function () {
      emitCaretState();
      paintSelectionForeground();
    });

    // Ctrl+S and friends belong to the host so they hit the same command pipeline as the
    // toolbar buttons. One table, bound to Monaco and to the page: see HOST_SHORTCUTS.
    registerHostShortcuts();

    // The precondition leaves Enter alone wherever a widget owns it, so accepting a find
    // result or a suggestion still works.
    state.editor.addCommand(
      monaco.KeyCode.Enter,
      continueList,
      'editorTextFocus && !suggestWidgetVisible && !renameInputVisible && !inSnippetMode');

    /*
      Alt on its own puts the keyboard on the menu bar, the way a Windows menu has always
      behaved. It is what makes the Alt shortcuts findable at all: press one key, the menus
      light up, and the arrows take it from there without anyone having to know a letter.

      Watched here rather than left to the host because a bare modifier never reaches XAML
      while the editor holds the keyboard, and the editor is where the caret nearly always
      is. Monaco cannot bind a lone modifier either, so this listens to the page directly.
    */
    var altAlone = false;

    window.addEventListener('keydown', function (e) {
      // Alt with anything else is a shortcut, not a request for the menu bar.
      altAlone = e.key === 'Alt' && !e.ctrlKey && !e.shiftKey && !e.metaKey;
    }, true);

    window.addEventListener('keyup', function (e) {
      if (e.key === 'Alt' && altAlone) { post('command', { name: 'menu.focus' }); }
      altAlone = false;
    }, true);

    /*
      The Find family, which HOST_SHORTCUTS deliberately leaves out.

      Ctrl+F, Ctrl+H, F3, Shift+F3 and Ctrl+G belong to Monaco's own widgets while the
      editor holds the keyboard, so they are not registered over the top of it. That leaves
      them silent in the two places a reader actually is: preview view, where the source
      pane is display:none and Monaco holds nothing, and split view with the keyboard in the
      preview. The WebView owns the keyboard in both, so XAML never sees the press either,
      and searching what you are reading did nothing at all.

      runFindCommand brings the source pane back and puts the keyboard in it, which is the
      answer to the same key in both views: there is one editor, and it is what searches.

      Bubble phase like the table's own listener, so Monaco still wins whenever it holds the
      keyboard - it stops the keys it has handled before they reach the page.
    */
    window.addEventListener('keydown', function (e) {
      if (!state.editor || e.altKey || e.metaKey) { return; }

      // Belt and braces for the bubble-phase rule above: with the editor focused these
      // never arrive, and if one ever did it would be Monaco's press to answer, not ours.
      if (state.editor.hasWidgetFocus && state.editor.hasWidgetFocus()) { return; }

      var command = null;

      if (e.ctrlKey && !e.shiftKey && e.code === 'KeyF') { command = 'find'; }
      else if (e.ctrlKey && !e.shiftKey && e.code === 'KeyH') { command = 'replace'; }
      else if (e.ctrlKey && !e.shiftKey && e.code === 'KeyG') { command = 'gotoLine'; }
      else if (!e.ctrlKey && e.code === 'F3') { command = e.shiftKey ? 'findPrevious' : 'findNext'; }

      if (!command) { return; }

      e.preventDefault();
      runFindCommand(command, findSeedFor(command));
    });

    // The host needs to know which pane the user is working in, so toolbar and keyboard
    // zoom commands act on the pane the user is actually looking at.
    state.editor.onDidFocusEditorWidget(function () {
      post('paneFocused', { pane: 'Source' });
    });

    els.previewPane.addEventListener('pointerdown', function () {
      post('paneFocused', { pane: 'Preview' });
    });

    wireCtrlWheelZoom(els.monacoHost, 'Source');
    wireCtrlWheelZoom(els.previewPane, 'Preview');

    wirePaneContextMenus();

    // Pulls the markdown grammar before the shell reports ready, so the first paint of a
    // large document is already tokenized rather than briefly plain.
    return monaco.editor.colorize('', 'markdown', {})
      .catch(function () { /* Warm-up only; a failure here is not interesting. */ })
      .then(function () {
        els.boot.classList.add('is-hidden');
        setTimeout(function () { els.boot.style.display = 'none'; }, 260);

        post('ready', { sourceFont: DEFAULT_FONTS.mono, previewFont: DEFAULT_FONTS.ui });

        reportResolvedFonts();
      });
  }

  // --------------------------------------------------------- edit commands

  /*
    The Edit menu's vocabulary, mapped onto Monaco's own actions. Keeping the mapping here
    rather than in the host means the menu stays free of editor implementation details.
  */
  var EDITOR_ACTIONS = {
    find: 'actions.find',
    replace: 'editor.action.startFindReplaceAction',
    findNext: 'editor.action.nextMatchFindAction',
    findPrevious: 'editor.action.previousMatchFindAction',
    gotoLine: 'editor.action.gotoLine',
    undo: 'undo',
    redo: 'redo'
  };

  /*
    Cut, copy and paste are deliberately absent from the table above.

    Monaco's clipboard actions go through document.execCommand, which a browser honours only
    during a trusted user gesture. A click on the native Edit menu arrives here as a bridge
    message carrying no user activation, so those actions silently do nothing. Typing Ctrl+C
    in the editor is a real gesture and works, which is exactly what makes the menu's silence
    easy to miss.

    The host therefore owns the clipboard: it asks for the selection, writes it to the
    Windows clipboard itself, and pushes text back in for a paste.
  */

  function currentSelectionText() {
    var editor = state.editor;
    if (!editor) { return ''; }

    var model = editor.getModel();
    var selection = editor.getSelection();

    if (!model || !selection || selection.isEmpty()) { return ''; }

    return model.getValueInRange(selection);
  }

  function deleteSelection() {
    var editor = state.editor;
    if (!editor) { return; }

    var selection = editor.getSelection();
    if (!selection || selection.isEmpty()) { return; }

    editor.executeEdits('marqora-cut', [{ range: selection, text: '', forceMoveMarkers: true }]);
  }

  function insertAtCursor(text) {
    var editor = state.editor;
    if (!editor) { return; }

    var selection = editor.getSelection();
    if (!selection) { return; }

    editor.executeEdits('marqora-paste', [{ range: selection, text: text, forceMoveMarkers: true }]);
    editor.focus();
  }

  /*
    Replaces the whole document as one undoable edit.

    model.setValue would be simpler but throws the undo stack away, so a format could not be
    taken back with Ctrl+Z - which is the first thing anyone does after a formatter surprises
    them. executeEdits against the full range keeps the history, and the undo stops either
    side collapse the whole reformat into a single step.

    The cursor goes back to the same line afterwards, with its column clamped: the line it
    was sitting on may well have grown or shrunk.
  */
  function replaceAllText(id, text) {
    var tab = state.tabs[id];
    if (!tab || !tab.model) { return; }

    var model = tab.model;
    if (model.getValue() === text) { return; }

    var editor = state.editor;
    var isActive = id === state.activeTabId && editor;
    var position = isActive ? editor.getPosition() : null;
    var scrollTop = isActive ? editor.getScrollTop() : 0;

    if (isActive) { editor.pushUndoStop(); }

    model.pushEditOperations(
      [],
      [{ range: model.getFullModelRange(), text: text, forceMoveMarkers: true }],
      function () { return null; }
    );

    if (isActive) {
      editor.pushUndoStop();

      if (position) {
        var line = Math.min(position.lineNumber, model.getLineCount());
        var column = Math.min(position.column, model.getLineMaxColumn(line));
        editor.setPosition({ lineNumber: line, column: column });
      }

      editor.setScrollTop(scrollTop);
    }
  }

  /// The zero-based line range the user has selected, or null when nothing is selected.
  function selectedLineRange() {
    var editor = state.editor;
    if (!editor) { return null; }

    var selection = editor.getSelection();
    if (!selection || selection.isEmpty()) { return null; }

    // A selection ending at column 1 stops before that line rather than on it.
    var endLine = selection.endColumn === 1 && selection.endLineNumber > selection.startLineNumber
      ? selection.endLineNumber - 1
      : selection.endLineNumber;

    return { startLine: selection.startLineNumber - 1, endLine: endLine - 1 };
  }

  /*
    The selection with column precision, plus the lines it covers and one either side.

    The host's copy of the document trails the editor by a debounce interval, so an
    authoring command that computed against it would act on text one keystroke out of
    date. Sending the lines along with the selection closes that gap, and a line of margin
    either side is what the block commands need to tell whether they already have a blank
    line to sit beside.

    Monaco counts lines and columns from one; the host counts from zero throughout.
  */
  function editContext(requestId) {
    var editor = state.editor;
    var model = editor && editor.getModel();
    var selection = editor && editor.getSelection();

    if (!model || !selection) { return { requestId: requestId }; }

    var first = Math.max(1, selection.startLineNumber - 1);
    var last = Math.min(model.getLineCount(), selection.endLineNumber + 1);
    var lines = [];

    for (var i = first; i <= last; i++) {
      lines.push(model.getLineContent(i));
    }

    return {
      requestId: requestId,
      firstLine: first - 1,
      lines: lines,
      startLine: selection.startLineNumber - 1,
      startColumn: selection.startColumn - 1,
      endLine: selection.endLineNumber - 1,
      endColumn: selection.endColumn - 1
    };
  }

  /*
    Applies a batch of edits as one undoable step.

    Every edit is addressed against the document as the host saw it, so they all go in at
    once rather than one after another. The undo stops either side collapse the batch into
    a single Ctrl+Z, the same way a reformat does.
  */
  function applyEdits(edits, selection) {
    var editor = state.editor;
    var model = editor && editor.getModel();

    if (!model || !edits || !edits.length) { return; }

    var eol = model.getEOL();
    var operations = [];

    for (var i = 0; i < edits.length; i++) {
      var edit = edits[i];

      operations.push({
        range: new state.monaco.Range(
          edit.startLine + 1, edit.startColumn + 1, edit.endLine + 1, edit.endColumn + 1),
        // The host works in plain newlines and knows nothing about this file's endings.
        text: String(edit.text).replace(/\n/g, eol),
        forceMoveMarkers: true
      });
    }

    editor.pushUndoStop();
    editor.executeEdits('marqora-authoring', operations);
    editor.pushUndoStop();

    if (selection) {
      editor.setSelection(new state.monaco.Selection(
        selection.startLine + 1, selection.startColumn + 1,
        selection.endLine + 1, selection.endColumn + 1));
    }

    editor.focus();
  }

  // indent, marker, ordered digits, ordered delimiter, task box, gap, content.
  var LIST_ITEM = /^(\s*)([-*+]|(\d+)([.)]))(\s+\[([ xX])\])?(\s+)(.*)$/;

  /*
    Carries a list on to the next line when Enter is pressed inside one.

    This is the one authoring behaviour the host cannot own. The decision depends on the
    line the caret is on at the instant the key goes down, and a round trip to the host
    would arrive after the newline had already been typed.
  */
  function continueList() {
    var editor = state.editor;
    var model = editor && editor.getModel();
    var selection = editor && editor.getSelection();

    if (!model || !selection) { return; }

    var line = model.getLineContent(selection.startLineNumber);
    var match = LIST_ITEM.exec(line);

    // Not in a list, the selection spans lines, or the preference is off: whatever Enter
    // normally does, including auto-indent, is what should happen. The command stays bound
    // either way rather than being unbound and rebound, so there is one code path to reason
    // about and no window in which Enter belongs to nobody.
    if (!state.continueLists || !match || selection.startLineNumber !== selection.endLineNumber) {
      editor.trigger('keyboard', 'type', { text: '\n' });
      return;
    }

    var content = match[8];

    // Enter on an item with nothing in it ends the list rather than making another one.
    if (!content) {
      editor.executeEdits('marqora-authoring', [{
        range: new state.monaco.Range(
          selection.startLineNumber, 1, selection.startLineNumber, line.length + 1),
        text: '',
        forceMoveMarkers: true
      }]);
      return;
    }

    var task = match[5];
    var ordered = match[3];
    var marker = ordered ? String(parseInt(ordered, 10) + 1) + match[4] : match[2];

    // A carried-over task box starts unticked however the one above it was left.
    var prefix = match[1] + marker + (task ? ' [ ] ' : match[7]);

    editor.executeEdits('marqora-authoring', [{
      range: selection,
      text: model.getEOL() + prefix,
      forceMoveMarkers: true
    }]);
  }

  /*
    Copies the preview's markup with every style written onto the elements themselves.

    Word applies the formatting a tag implies -- bold is bold, a table is a table -- but it
    will not be relied on to parse a stylesheet. Its CSS support predates most of what
    app.css uses, and anything carried by a class rather than a tag arrives unstyled, which
    is every callout colour, every code background and the whole of the syntax
    highlighting.

    So the browser is asked instead. The preview is already laid out with the real CSS
    applied, so getComputedStyle knows the answer for every element; writing those values
    into style attributes leaves markup that needs no stylesheet at all.
  */
  var INLINE_STYLE_PROPERTIES = [
    'color', 'background-color',
    'font-family', 'font-size', 'font-style', 'font-weight', 'font-variant',
    'text-decoration-line', 'text-align', 'line-height', 'white-space', 'vertical-align',
    'margin-top', 'margin-right', 'margin-bottom', 'margin-left',
    'padding-top', 'padding-right', 'padding-bottom', 'padding-left',
    'border-top-width', 'border-top-style', 'border-top-color',
    'border-right-width', 'border-right-style', 'border-right-color',
    'border-bottom-width', 'border-bottom-style', 'border-bottom-color',
    'border-left-width', 'border-left-style', 'border-left-color',
    'list-style-type'
  ];

  // Inherited properties are only worth writing when they differ from the parent, which
  // keeps the markup from repeating the body font on every span in the document.
  var INHERITED_PROPERTIES = {
    'color': true, 'font-family': true, 'font-size': true, 'font-style': true,
    'font-weight': true, 'font-variant': true, 'text-align': true, 'line-height': true,
    'white-space': true, 'list-style-type': true
  };

  // Values that mean "nothing here", not worth the bytes.
  var EMPTY_VALUES = {
    'none': true, '0px': true, 'normal': true, 'auto': true,
    'rgba(0, 0, 0, 0)': true, 'transparent': true
  };

  function withInlineStyles(source) {
    // The clone is measured rather than the original, so the live preview is never touched.
    // It has to be in the document for getComputedStyle to have anything to say, and it
    // keeps mq-preview so the same rules apply to it.
    var stage = document.createElement('div');
    stage.className = 'mq-preview';
    stage.setAttribute('aria-hidden', 'true');
    stage.style.cssText = 'position:absolute;left:-99999px;top:0;width:46em;';
    stage.innerHTML = source.innerHTML;

    /*
      Light for the duration, whatever the app is showing.

      The palette lives on :root, so anything measured while the app is dark comes back as
      light text on a dark ground -- which would then be pasted into somebody's white
      document. The exports have always pinned light for the same reason; this is that rule
      applied to the clipboard, which now takes its colours from the page rather than from
      a stylesheet that could be pinned on the host side.

      Restored before returning, and no paint happens in between, so nothing flickers.
    */
    var root = document.documentElement;
    var theme = root.getAttribute('data-theme');
    root.setAttribute('data-theme', 'light');

    document.body.appendChild(stage);

    try {
      var elements = stage.querySelectorAll('*');

      for (var i = 0; i < elements.length; i++) {
        applyInlineStyle(elements[i]);
      }

      return stage.innerHTML;
    } catch (err) {
      report('warning', 'Could not inline styles for the clipboard: ' + err.message, err.stack);

      // Better to hand over markup that leans on the stylesheet than nothing at all.
      return source.innerHTML;
    } finally {
      document.body.removeChild(stage);

      if (theme === null) { root.removeAttribute('data-theme'); }
      else { root.setAttribute('data-theme', theme); }
    }
  }

  /*
    Flattens a translucent colour onto white.

    Word's HTML parser is old enough to predate rgba(), and drops a declaration it cannot
    read rather than approximating it -- so every tinted callout background would simply
    vanish. Compositing here against the white the fragment is pinned to gives the same
    colour in a form anything can read.
  */
  function opaque(value) {
    var parts = /^rgba\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*\)$/.exec(value);
    if (!parts) { return value; }

    var alpha = parseFloat(parts[4]);
    if (alpha >= 1) { return 'rgb(' + parts[1] + ', ' + parts[2] + ', ' + parts[3] + ')'; }

    function blend(channel) {
      return Math.round(parseFloat(channel) * alpha + 255 * (1 - alpha));
    }

    return 'rgb(' + blend(parts[1]) + ', ' + blend(parts[2]) + ', ' + blend(parts[3]) + ')';
  }

  function applyInlineStyle(element) {
    // SVG carries its own presentation attributes and its style property behaves
    // differently; mermaid diagrams already arrive self-describing.
    if (element.namespaceURI && element.namespaceURI.indexOf('svg') >= 0) { return; }

    var computed = window.getComputedStyle(element);
    var parent = element.parentElement ? window.getComputedStyle(element.parentElement) : null;
    var declarations = [];

    for (var i = 0; i < INLINE_STYLE_PROPERTIES.length; i++) {
      var name = INLINE_STYLE_PROPERTIES[i];
      var value = computed.getPropertyValue(name);

      if (!value) { continue; }

      if (INHERITED_PROPERTIES[name]) {
        if (parent && parent.getPropertyValue(name) === value) { continue; }
      } else if (EMPTY_VALUES[value]) {
        continue;
      }

      declarations.push(name + ':' + opaque(value));
    }

    if (declarations.length) {
      var existing = element.getAttribute('style');
      element.setAttribute('style', existing ? existing + ';' + declarations.join(';') : declarations.join(';'));
    }
  }

  /*
    The Find family: find, replace, find next, find previous and go to line.

    Held apart from the rest of the Edit menu because each of them puts something on screen
    that the user then works in - a widget, or a selection they have to be able to see. Two
    things follow from that, and neither applies to undo or select-all:

      - the source pane has to be laid out before the command runs, not merely asked for.
        A widget opened on a pane that is still display:none opens nowhere, and Monaco's
        focus() on a hidden editor does nothing at all;

      - nothing may take the keyboard back afterwards. The ordinary showSource switches view
        with takeFocus, which lands a focusPane in the editor a moment later and pulls the
        caret straight back out of the search box.

    So this posts showSourceForFind, whose host side switches to split without taking focus,
    parks the command, and lets setViewMode run it once the pane has a size.
  */
  var FIND_COMMANDS = {
    find: true,
    replace: true,
    findNext: true,
    findPrevious: true,
    gotoLine: true
  };

  function runFindCommand(command, seed) {
    if (!state.editor) { return; }

    if (state.viewMode === 'Preview') {
      state.pendingFind = { command: command, seed: seed || '' };
      post('command', { name: 'showSourceForFind' });
      return;
    }

    applyFindCommand(command, seed);
  }

  function applyFindCommand(command, seed) {
    var editor = state.editor;
    if (!editor) { return; }

    // In split view the preview may be holding the keyboard, which is the other half of why
    // these arrive here at all: an editor answers a keystroke only while it has focus.
    editor.focus();

    var actionId = EDITOR_ACTIONS[command];
    var action = actionId ? editor.getAction(actionId) : null;

    if (!action) {
      report('warning', 'Editor action is unavailable: ' + (actionId || command));
      return;
    }

    var done = action.run();

    if (!seed) { return; }

    if (done && typeof done.then === 'function') {
      done.then(function () { seedFindTerm(seed); });
    } else {
      seedFindTerm(seed);
    }
  }

  /*
    Puts a term in the find box, over whatever the action seeded from the editor's own
    selection - which, for a search started from the preview, is wherever the caret was left
    rather than what the user is looking at.

    Set after the action has run rather than before, because the action seeds the box itself
    as it opens and would overwrite anything written first.
  */
  function seedFindTerm(term) {
    var editor = state.editor;
    if (!editor) { return; }

    try {
      var controller = editor.getContribution('editor.contrib.findController');

      if (controller && typeof controller.setSearchString === 'function') {
        controller.setSearchString(term);
      }
    } catch (err) {
      report('warning', 'Could not seed the find box', err && err.message);
    }
  }

  /*
    What is selected in the preview, when it is worth searching for.

    Ctrl+Shift+F seeds Find All from the editor's selection; this is the preview's half of
    the same idea. A search started from the rendered text should look for the words the
    user has their eye on, not for whatever the source pane was last left on.

    Single line only, matching SelectedTermAsync in the view model - a selection spanning
    lines is not a search term. Rendered text is not always source text either, so a term
    that crosses markup finds nothing; the box is then open with the term in it, which is a
    better place to correct it from than an empty one.
  */
  function previewSelectionTerm() {
    var dom = window.getSelection();

    if (!dom || dom.isCollapsed || dom.rangeCount === 0) { return ''; }
    if (!els.preview.contains(dom.anchorNode) || !els.preview.contains(dom.focusNode)) { return ''; }

    var text = String(dom).trim();

    return (text && text.indexOf('\n') < 0) ? text : '';
  }

  // Only the two that open a search box are seeded. Go to line has nothing to seed, and
  // stepping a search is meant to repeat the term already in the box.
  function findSeedFor(command) {
    return (command === 'find' || command === 'replace') ? previewSelectionTerm() : '';
  }

  function runEditorCommand(command) {
    var editor = state.editor;
    if (!editor) { return; }

    // The Find family has its own route, so a command picked from the menu and the same one
    // typed as a shortcut arrive at the widget on identical terms.
    if (FIND_COMMANDS[command]) {
      runFindCommand(command, findSeedFor(command));
      return;
    }

    // These commands act on the source pane, so bring it into view first; otherwise the
    // edit would land on a pane the user cannot see.
    if (state.viewMode === 'Preview') {
      post('command', { name: 'showSource' });
    }

    editor.focus();

    if (command === 'selectAll') {
      var model = editor.getModel();
      if (model) { editor.setSelection(model.getFullModelRange()); }
      return;
    }

    var actionId = EDITOR_ACTIONS[command];
    if (!actionId) {
      report('warning', 'Unknown edit command: ' + command);
      return;
    }

    // undo and redo are editor triggers rather than registered actions.
    if (command === 'undo' || command === 'redo') {
      editor.trigger('menu', actionId, null);
      return;
    }

    var action = editor.getAction(actionId);

    if (action) {
      action.run();
    } else {
      report('warning', 'Editor action is unavailable: ' + actionId);
    }
  }

  // ------------------------------------------------------------------- tabs

  /*
    Each tab owns a Monaco text model. Switching tabs swaps the model on the single editor
    and restores the view state captured when the tab was last left, so undo history,
    selection and scroll position all survive. Creating models is cheap; re-tokenizing a
    large document on every switch would not be.
  */

  function openTab(id, text, html) {
    if (!state.monaco || state.tabs[id]) { return; }

    state.tabs[id] = {
      model: state.monaco.editor.createModel(text, 'markdown'),
      viewState: null,
      html: html,
      previewScrollTop: 0,
      // Decoration ids for this tab's selected text. See paintSelectionForeground.
      selectionInk: [],

      // Decoration ids for this tab's misspellings. See setSpelling.
      spellInk: []
    };
  }

  function activateTab(id) {
    var tab = state.tabs[id];

    // Being asked for a tab that was never opened means the host sent activate before open.
    // That used to fail silently and leave a blank window, so it is reported rather than
    // swallowed.
    if (!tab) {
      report('warning', 'Asked to activate an unknown tab', String(id));
      return;
    }

    if (!state.editor) { return; }

    if (state.activeTabId === id) {
      // Still refresh the preview: the folder mapping may have changed underneath it.
      state.lastHtml = null;
      applyPreviewHtml(tab.html, false);
      return;
    }

    rememberActiveTab();

    state.activeTabId = id;
    state.suppressEditorEvents = true;
    state.editor.setModel(tab.model);
    state.suppressEditorEvents = false;

    if (tab.viewState) {
      state.editor.restoreViewState(tab.viewState);
    }

    // The tab's own selection is back, and the theme may have changed while it was away.
    paintSelectionForeground();

    // Force a redraw even when the HTML matches the outgoing tab's.
    state.lastHtml = null;
    applyPreviewHtml(tab.html, false);

    els.previewPane.scrollTop = tab.previewScrollTop || 0;

    // The WebView is collapsed by the host until a document exists, so the page may have
    // laid out at zero size. Monaco caches its dimensions and renders nothing when those
    // are stale, so it is told to re-measure every time a tab is brought up.
    state.editor.layout();

    updateWrapGlyphs();
    emitStats();
    emitCaretState();
    reportLayout('activateTab');
  }

  /// Captures the outgoing tab's cursor, scroll and preview position.
  function rememberActiveTab() {
    var current = state.activeTabId ? state.tabs[state.activeTabId] : null;
    if (!current || !state.editor) { return; }

    current.viewState = state.editor.saveViewState();
    current.previewScrollTop = els.previewPane.scrollTop;
  }

  function closeTab(id) {
    var tab = state.tabs[id];
    if (!tab) { return; }

    // A model still attached to the editor must be detached before disposal.
    if (state.activeTabId === id) {
      state.activeTabId = null;
      if (state.editor) { state.editor.setModel(null); }
    }

    tab.model.dispose();
    delete state.tabs[id];
  }

  /// Blanks the surface when the last tab closes, so no stale document lingers behind.
  function clearSurface() {
    rememberActiveTab();

    state.activeTabId = null;
    state.lastHtml = null;

    if (state.editor) { state.editor.setModel(null); }

    els.preview.innerHTML = '';
    els.previewPane.scrollTop = 0;
    state.lineMapDirty = true;
    updateWrapGlyphs();
  }

  /*
    Squiggles, by owner.

    Monaco keeps markers per model and per owner string, and setModelMarkers replaces an
    owner's whole set rather than adding to it. Document problems and spelling are switched
    on and off independently, so they own separate names: publishing one under the other's
    name would silently wipe it.

    Markers hang off the model rather than the editor, so a tab that is not on screen can be
    marked up without being brought forward, exactly as updatePreview already updates its HTML.
  */
  function setMarkers(id, owner, markers) {
    var tab = state.tabs[id];
    if (!tab || !tab.model || !state.monaco) { return; }

    state.monaco.editor.setModelMarkers(tab.model, owner, markers || []);
  }

  function clearMarkers(owner) {
    if (!state.monaco) { return; }

    for (var id in state.tabs) {
      if (Object.prototype.hasOwnProperty.call(state.tabs, id) && state.tabs[id].model) {
        state.monaco.editor.setModelMarkers(state.tabs[id].model, owner, []);
      }
    }
  }

  // ---------------------------------------------------------------- inbound

  /*
    Names the open file in the page title.

    Nothing of it reaches paper. A printed page carries the document and nothing else: no
    header, no footer, no letterhead. The title is set because it is what the print queue
    lists the job under, which is the one place a name is still useful.
  */
  function setPrintSource(path) {
    document.title = path ? ('Marqora - ' + path) : 'Marqora';
  }

  var handlers = {
    openTab: function (p) {
      openTab(p.id, p.text || '', p.html || '');
      reportLayout('openTab');
    },

    /// Names the diagrams that have a pop-out window open and so are worth reporting.
    watchDiagrams: function (p) {
      setWatchedDiagrams(p.items || []);

      // A window may have opened onto a document that is not the one on screen, or onto a
      // diagram whose markup has moved on since; this pushes the current state at it.
      reportDiagrams();
    },

    activateTab: function (p) {
      state.documentBaseUrl = p.documentBaseUrl || '';
      setPrintSource(p.documentPath || '');
      activateTab(p.id);
    },

    closeTab: function (p) {
      // Before the tab goes: afterwards there is nothing left to say which document it was.
      reportDiagramsGone(p.id);
      closeTab(p.id);
    },

    updatePreview: function (p) {
      var tab = state.tabs[p.id];
      if (!tab) { return; }

      tab.html = p.html || '';

      // A background tab keeps its new HTML but is not redrawn until it is shown.
      if (p.id === state.activeTabId) {
        applyPreviewHtml(tab.html, false);
      }
    },

    setTabText: function (p) {
      var tab = state.tabs[p.id];
      if (!tab) { return; }

      state.suppressEditorEvents = true;
      if (tab.model.getValue() !== p.text) { tab.model.setValue(p.text); }
      state.suppressEditorEvents = false;

      tab.html = p.html || '';

      if (p.id === state.activeTabId) {
        state.lastHtml = null;
        applyPreviewHtml(tab.html, false);
        emitStats();

        // setValue throws the undo stack away, so the toolbar's Undo has to be told to go
        // grey. Nothing else will say so: the change event above is suppressed for
        // host-driven writes, and that is what normally carries the caret state.
        emitCaretState();
      }
    },

    clearSurface: function () {
      clearSurface();
    },

    setViewMode: function (p) {
      state.viewMode = p.mode;
      els.root.setAttribute('data-view', p.mode);
      state.lineMapDirty = true;

      if (state.editor) { state.editor.layout(); }

      /*
        A Find command that was waiting for this. The pane has just been laid out, so the
        widget has somewhere to open and focus() has something to land on; running it any
        earlier is what parking it was for. See runFindCommand.
      */
      if (state.pendingFind && p.mode !== 'Preview') {
        var pending = state.pendingFind;
        state.pendingFind = null;
        applyFindCommand(pending.command, pending.seed);
      }

      if (p.mode === 'SideBySide') {
        requestAnimationFrame(syncEditorToPreview);
      }

      // Temporary diagnostic for the blank-preview report. Reads the pane after layout has
      // settled, so it distinguishes "no content" from "content the user cannot see".
      requestAnimationFrame(function () {
        try {
          var pane = els.previewPane;
          var active = state.tabs[state.activeTabId] || {};

          report('info', 'view=' + p.mode
            + ' domHtml=' + (els.preview.innerHTML || '').length
            + ' children=' + els.preview.childElementCount
            + ' tabHtml=' + ((active.html || '').length)
            + ' pane=' + Math.round(pane.clientWidth) + 'x' + Math.round(pane.clientHeight)
            + ' scrollTop=' + Math.round(pane.scrollTop)
            + ' scrollH=' + Math.round(pane.scrollHeight)
            + ' article=' + Math.round(els.preview.clientWidth) + 'x' + Math.round(els.preview.clientHeight)
            + ' tab=' + (state.activeTabId || 'none'));
        } catch (err) {
          report('warning', 'View-mode diagnostic failed', err && err.message);
        }
      });
    },

    setTheme: function (p) {
      state.theme = p.theme;
      document.documentElement.setAttribute('data-theme', p.theme === 'Dark' ? 'dark' : 'light');

      /*
        The two colours a match is drawn in come from the host, not from app.css: the Find
        All window paints the same pair with WinUI brushes, and the app keeps them in one
        place - MatchColors in the app project - rather than writing them down twice.

        They are put into the palette as custom properties so everything downstream reads
        them the way it reads every other colour: defineThemes below, and any stylesheet
        rule that wants them. Set on the element rather than in a rule, so they hold in
        both themes and there is nothing to re-point.
      */
      if (p.selection) {
        document.documentElement.style.setProperty('--mq-selection', p.selection);
      }

      if (p.selectionText) {
        document.documentElement.style.setProperty('--mq-selection-text', p.selectionText);
      }

      applyHighlightTheme();

      if (state.monaco) {
        defineThemes(state.monaco);
        state.monaco.editor.setTheme(monacoThemeName());
      }

      // Light mode drops the ink, dark mode lays it down; either way the selection that is
      // already on screen has to be repainted rather than waiting for the next one.
      paintSelectionForeground();

      // Diagrams bake their colours into the SVG, so they must be produced again. The
      // module stays loaded; only its theme configuration is replaced.
      mermaidCache = {};
      reinitializeMermaid();

      var processed = els.preview.querySelectorAll('pre.mermaid[data-processed]');
      for (var i = 0; i < processed.length; i++) {
        processed[i].removeAttribute('data-processed');
      }

      var html = state.lastHtml;
      state.lastHtml = null;
      if (html !== null) { applyPreviewHtml(html, false); }
    },

    setZoom: function (p) {
      if (p.pane === 'Source') { applySourceZoom(p.percent, false); }
      else { applyPreviewZoom(p.percent, false); }
    },

    setScrollSync: function (p) {
      state.scrollSync = !!p.enabled;
      if (state.scrollSync) { syncEditorToPreview(); }
    },

    /*
      Whether the outline panel is open and wants to know where the preview is.

      Turning it on reports straight away rather than waiting for a scroll: the panel has
      just appeared and has to highlight something, and the document may not be touched for
      a while. The remembered line is cleared first so that report is never suppressed as a
      repeat of one sent before the panel was closed.
    */
    setOutlineTracking: function (p) {
      state.outlineTracking = !!p.enabled;
      state.reportedViewportLine = -1;

      if (state.outlineTracking) { reportViewportLine(); }
    },

    setWordWrap: function (p) {
      state.wordWrap = !!p.enabled;

      if (state.editor) { state.editor.updateOptions({ wordWrap: p.enabled ? 'on' : 'off' }); }

      // Nothing wraps when wrapping is off, so the markers go with it.
      updateWrapGlyphs();
    },

    setLineNumbers: function (p) {
      if (state.editor) { state.editor.updateOptions({ lineNumbers: p.enabled ? 'on' : 'off' }); }
      updateWrapGlyphs();
    },

    setShowWhitespace: function (p) {
      if (state.editor) {
        state.editor.updateOptions({ renderWhitespace: p.enabled ? 'all' : 'selection' });
      }
    },

    setWrapGlyph: function (p) {
      state.showWrapGlyph = !!p.enabled;
      updateWrapGlyphs();
    },

    /*
      Everything the preferences dialog owns, in one message.

      The View-menu toggles above each arrive on their own because each is driven by its own
      menu item. These arrive together because they are set together, and because several of
      them are CSS custom properties rather than editor options - handling them one at a time
      would mean a message per property for no gain.

      A font family of null clears the custom property rather than writing an empty one,
      which is what hands the stylesheet's own stack back. Writing '' would leave a declared
      but empty family, and Monaco would then render in whatever it falls back to instead of
      in --mq-font-mono.
    */
    applyPreferences: function (p) {
      var root = document.documentElement;

      setFontProperty(root, '--mq-font-mono', p.sourceFont);
      setFontProperty(root, '--mq-font-ui', p.previewFont);

      if (p.previewFontSize) {
        root.style.setProperty('--mq-preview-base', String(p.previewFontSize) + 'px');
      }

      // Zero means no limit, which is the shipped behaviour: the preview fills its pane.
      if (p.previewMaxWidth > 0) {
        root.style.setProperty('--mq-preview-measure', String(p.previewMaxWidth) + 'px');
      } else {
        root.style.removeProperty('--mq-preview-measure');
      }

      state.continueLists = p.continueLists !== false;
      state.sourceFontBase = p.sourceFontSize || SOURCE_BASE_FONT_PX;

      // Re-numbered here rather than waiting for the next render, so switching the
      // preference shows on the document already in front of the user.
      state.headingNumberStart = p.headingNumbers || 0;
      numberHeadings();

      if (state.editor) {
        state.editor.updateOptions({
          // The zoom is folded back in here: this runs whenever a preference changes, and
          // the user may well be zoomed at the time.
          fontSize: state.sourceFontBase * (state.sourceZoom / 100),
          fontFamily: getComputedStyle(root).getPropertyValue('--mq-font-mono').trim(),
          tabSize: p.tabSize || 4,
          insertSpaces: p.insertSpaces !== false,
          minimap: { enabled: !!p.minimap },
          renderLineHighlight: p.highlightCurrentLine === false ? 'none' : 'line',
          autoClosingBrackets: p.autoCloseBrackets === false ? 'never' : 'languageDefined',
          autoClosingQuotes: p.autoCloseBrackets === false ? 'never' : 'languageDefined',
          autoSurround: p.autoCloseBrackets === false ? 'never' : 'languageDefined'
        });
      }

      // A font or measure change moves every line, so the source-to-preview map is stale.
      state.lineMapDirty = true;
      updateWrapGlyphs();

      // The chosen font may not be installed, in which case something else is on screen.
      // The dialog is open right now and is the only thing that can say so.
      reportResolvedFonts();
    },

    editorCommand: function (p) {
      runEditorCommand(p.command);
    },

    /*
      The only request-and-reply message in the bridge. Export needs the preview exactly as
      rendered, diagrams and maths included, so the host asks for it and matches the reply
      by request id rather than assuming the next message back is the answer.
    */
    requestRenderedHtml: function (p) {
      post('renderedHtml', { requestId: p.requestId, html: els.preview.innerHTML });
    },

    requestSelectionRange: function (p) {
      var range = selectedLineRange();

      post('selectionRange', {
        requestId: p.requestId,
        startLine: range ? range.startLine : -1,
        endLine: range ? range.endLine : -1
      });
    },

    requestEditContext: function (p) {
      post('editContext', editContext(p.requestId));
    },

    /*
      Squiggles for broken links, missing images and the style rules the formatter would
      fix. Markers hang off the model rather than the editor, so a tab that is not on
      screen can be marked up without being brought forward, exactly as updatePreview
      already updates its HTML.
    */
    setDiagnostics: function (p) {
      setMarkers(p.id, 'marqora-lint', p.markers);
    },

    clearDiagnostics: function () {
      clearMarkers('marqora-lint');
    },

    /*
      Misspellings, drawn as decorations rather than as markers.

      Markers would bring a hover that repeats what the squiggle already says, an Alt+F8 peek
      panel to dismiss, a "No quick fixes available" line that is untrue, and a place in the
      document's problem count beside the dead links - which is a different kind of thing. A
      decoration draws the line and brings none of it. See app.css for the squiggle itself.

      Like markers, decorations hang off the model, so a tab that is not on screen can be marked
      up without being brought forward.
    */
    setSpelling: function (p) {
      var tab = state.tabs[p.id];
      if (!tab || !tab.model || !state.monaco) { return; }

      var issues = p.issues || [];
      var ink = [];

      for (var i = 0; i < issues.length; i++) {
        var issue = issues[i];

        ink.push({
          // The host counts from zero; Monaco counts from one. Converted here, as applyEdits
          // does, rather than on the way out.
          range: new state.monaco.Range(
            issue.line + 1,
            issue.start + 1,
            issue.line + 1,
            issue.start + issue.length + 1),
          options: {
            inlineClassName: issue.repeated ? 'mq-repeated-word' : 'mq-misspelled',
            description: 'marqora-spelling',
            // Typing at either edge of a flagged word should not drag the squiggle along with
            // it; the next check will say where the word now ends.
            stickiness: state.monaco.editor.TrackedRangeStickiness.NeverGrowsWhenTypingAtEdges,

            /*
              A tick in the scrollbar for every misspelling, so a document can be scanned for
              them without scrolling through it. Markers gave these for nothing; a decoration
              has to ask.

              Named as a theme colour rather than given a hex value, so Monaco resolves it
              against whichever theme is in force and the ticks follow a theme change with no
              re-check. editorInfo.foreground is set from --mq-danger in defineThemes, which is
              also where the squiggle's red comes from - one place chooses the colour.
            */
            overviewRuler: {
              color: { id: 'editorInfo.foreground' },
              position: state.monaco.editor.OverviewRulerLane.Right
            }
          }
        });
      }

      tab.spellInk = tab.model.deltaDecorations(tab.spellInk || [], ink);
    },

    clearSpelling: function () {
      for (var id in state.tabs) {
        if (Object.prototype.hasOwnProperty.call(state.tabs, id)) {
          var tab = state.tabs[id];

          if (tab.model && tab.spellInk && tab.spellInk.length) {
            tab.spellInk = tab.model.deltaDecorations(tab.spellInk, []);
          }
        }
      }
    },

    applyEdits: function (p) {
      applyEdits(p.edits, p.selection);
    },

    /*
      Like setTabText, but as an undoable edit rather than a wholesale reset.

      The formatter uses this so Ctrl+Z takes a reformat back in one step. Editor events are
      suppressed because the host already knows the new text - it produced it - and echoing
      it back would render the document a second time for nothing.
    */
    replaceText: function (p) {
      var tab = state.tabs[p.id];

      // Reported rather than ignored. A formatted document that silently fails to arrive
      // looks exactly like a formatter that decided there was nothing to do, and the two
      // need telling apart from the log alone.
      if (!tab) {
        report('warning', 'replaceText: no editor model for tab ' + p.id);
        return;
      }

      state.suppressEditorEvents = true;
      replaceAllText(p.id, p.text || '');
      state.suppressEditorEvents = false;

      tab.html = p.html || '';

      if (p.id === state.activeTabId) {
        state.lastHtml = null;
        applyPreviewHtml(tab.html, false);
        emitStats();

        // A reformat is one undoable edit, so Undo has to come back to life - and every
        // mark around the caret may have moved. As with setTabText, the change event that
        // would normally report both is suppressed for host-driven writes.
        emitCaretState();
      }
    },

    requestSelection: function (p) {
      if (state.viewMode === 'Preview') { post('command', { name: 'showSource' }); }

      var text = currentSelectionText();

      if (p.cut) { deleteSelection(); }

      post('selectionCopied', { text: text });
    },

    /*
      The preview's half of the clipboard story.

      Same reply message as the editor's, because the host does the same thing with it -
      the clipboard is written host-side, since a browser only honours a copy during a
      trusted gesture and a click on a native menu is not one.

      Unlike requestSelection this does not pull the source pane into view: the user is
      copying from the preview and has no reason to be moved off it.
    */
    requestPreviewSelection: function () {
      var dom = window.getSelection();
      var text = '';

      if (dom && !dom.isCollapsed && dom.rangeCount > 0
          && els.preview.contains(dom.getRangeAt(0).commonAncestorContainer)) {
        text = dom.toString();
      }

      post('selectionCopied', { text: text });
    },

    /*
      The same question asked of the markup rather than the words: whatever is selected in
      the preview, as HTML, or the whole document when nothing is.

      The selected range is cloned into a detached element so its markup can be read
      without disturbing the live one. Rendering has already happened either way, so
      diagrams come across as inline SVG and code keeps its highlighting.
    */
    requestPreviewHtml: function (p) {
      var dom = window.getSelection();
      var text = '';
      var source = els.preview;

      if (dom && !dom.isCollapsed && dom.rangeCount > 0
          && els.preview.contains(dom.getRangeAt(0).commonAncestorContainer)) {
        source = document.createElement('div');
        source.className = 'mq-preview';
        source.appendChild(dom.getRangeAt(0).cloneContents());

        text = dom.toString();
      }

      post('previewHtml', {
        requestId: p.requestId,
        html: withInlineStyles(source),
        text: text
      });
    },

    selectAllInPreview: function () {
      var range = document.createRange();
      range.selectNodeContents(els.preview);

      var dom = window.getSelection();
      if (!dom) { return; }

      dom.removeAllRanges();
      dom.addRange(range);

      // Otherwise the next keystroke goes wherever focus happened to be, and the
      // selection the user just asked for is dropped without being used.
      els.preview.focus({ preventScroll: true });
    },

    insertText: function (p) {
      if (state.viewMode === 'Preview') { post('command', { name: 'showSource' }); }

      insertAtCursor(p.text || '');
    },

    resetSplitter: function () {
      if (splitterReset) { splitterReset(); }
    },

    /*
      Jump to the top or the bottom.

      The host decides which pane to move and whether both should go, so the rule about
      following the active pane lives in one place rather than being split across the two
      sides of the bridge.
    */
    scrollToEdge: function (p) {
      var toEnd = p.edge === 'end';
      var both = p.both === true;
      var wantsPreview = both || p.pane !== 'Source';
      var wantsSource = both || p.pane === 'Source';

      if (wantsPreview) {
        beginSync('source');
        els.previewPane.scrollTop = toEnd ? els.previewPane.scrollHeight : 0;
      }

      if (wantsSource && state.editor) {
        var model = state.editor.getModel();

        if (model) {
          // revealLine rather than setScrollTop: the editor knows where a line sits once
          // wrapping is taken into account, which a raw offset would not.
          var line = toEnd ? model.getLineCount() : 1;
          state.editor.revealLine(line);
          state.editor.setPosition({ lineNumber: line, column: 1 });
        }
      }
    },

    setSplitterPosition: function (p) {
      document.documentElement.style.setProperty('--mq-split', String(clamp(p.position, 0.15, 0.85)));
      state.lineMapDirty = true;
      if (state.editor) { state.editor.layout(); }
    },

    scrollToLine: function (p) {
      if (state.editor) {
        // At the top, not near it. revealLineNearTop leaves a gap above of whichever is
        // larger, five lines or a fifth of the pane, so the heading lands somewhere between
        // a fifth and a third of the way down depending on how tall the pane happens to be -
        // while scrollPreviewToLine below puts that same heading at the very top of the
        // preview. One heading, two panes, two places. A heading is the start of a section
        // and what sits above it is the section just left, so both panes go to the top.
        var line = p.line + 1;
        state.editor.revealRangeAtTop(new state.monaco.Range(line, 1, line, 1));
        state.editor.setPosition({ lineNumber: line, column: 1 });
      }
      beginSync('source');
      scrollPreviewToLine(p.line);
    },

    /*
      Selects a span in the source pane and shows it, for a result picked in the Find All
      window.

      The id is a guard rather than a lookup. Results are a snapshot of documents that go on
      being edited and switched between, so a pick can arrive after the editor has moved to
      another tab, and selecting the range on whichever model happens to be current would
      put a selection somewhere nobody pointed at. The host activates the right tab first;
      this drops anything that still does not match.

      The preview is left alone deliberately. Revealing the range scrolls the editor, and the
      editor's own scroll handler already carries the preview along when sync is on. A match
      that was on screen already scrolls nothing, which is the right answer for both panes.
    */
    selectRange: function (p) {
      var editor = state.editor;
      if (!editor || state.activeTabId !== p.id) { return; }

      // Nothing here asks for the source pane. A selection on a pane that is not on screen
      // is an answer the user cannot see, so the host puts the shell into split view first
      // and only then sends this - by which time setViewMode has already laid the editor
      // out, and revealing the line below scrolls a pane that has a size.

      var range = {
        startLineNumber: p.line + 1,
        startColumn: p.column + 1,
        endLineNumber: p.line + 1,
        endColumn: p.column + p.length + 1
      };

      editor.setSelection(range);
      editor.revealRangeInCenterIfOutsideViewport(range);

      // Only when the user asked to be taken there. Stepping through a results list leaves
      // the keyboard in the Find All window, where the next arrow key belongs.
      if (p.focus) { editor.focus(); }
    },

    /*
      Puts the keyboard in one of the two panes.

      The pane asked for is the one that last had it, which the host tracks from the
      paneFocused messages below. It can name a pane that is not on screen - the view mode
      may have changed since, or the keyboard may have been in the preview when the window
      was put into source-only view - and a hidden pane cannot take the keyboard. The view
      mode is known here and not there, so the fallback is decided here: whichever pane is
      actually showing gets it.

      The XAML half of this is done before the message is sent. Monaco's focus() moves the
      caret within the page, but if the WebView2 element does not hold XAML focus the page
      never sees a keystroke at all; see FocusWebView in WebViewPreviewHost.
    */
    focusPane: function (p) {
      var wantPreview = p.pane === 'Preview';

      if (state.viewMode === 'Source') { wantPreview = false; }
      else if (state.viewMode === 'Preview') { wantPreview = true; }

      // preventScroll: this is handing back a keyboard, not a request to go anywhere, and
      // the pane is already where the user left it.
      if (wantPreview) { els.preview.focus({ preventScroll: true }); }
      else if (state.editor) { state.editor.focus(); }
    }
  };

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

  // ------------------------------------------------------------------- boot

  require.config({ paths: { vs: 'vendor/monaco/vs' } });

  require(['vs/editor/editor.main'], function () {
    try {
      // Monaco is fully loaded, so the AMD marker has done its job and now only gets in
      // the way of the libraries loaded later. See releaseAmdMarker above.
      releaseAmdMarker();

      createEditor(window.monaco).catch(function (err) {
        report('error', 'Editor start-up failed: ' + err.message, err.stack);
      });
    } catch (err) {
      report('error', 'Editor creation failed: ' + err.message, err.stack);
    }
  }, function (err) {
    report('error', 'Monaco failed to load', err && err.message);
    els.boot.querySelector('.mq-boot__label').textContent = 'Editor failed to load';
  });
}());
