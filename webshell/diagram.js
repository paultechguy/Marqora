/*
  The diagram pop-out.

  Receives one SVG from the host and shows it, with zoom from 25% to 500%. Nothing else
  happens in this window: no mermaid, no markdown pipeline, no editor.
*/

(function () {
  'use strict';

  var MIN = 0.25;
  var MAX = 5;

  /*
    How far Fit may shrink a diagram, which is further than the zoom buttons go. A wide
    flowchart can need well under 25% to fit across a window, and holding the fit at the
    buttons' floor left it opening with its right side off-screen and a scrollbar, at a zoom
    that read as though it had been fitted. Below this, the labels are past reading anyway.
  */
  var FIT_MIN = 0.05;

  // Familiar browser-zoom stops rather than a fixed multiplier, so the steps land on round
  // percentages the toolbar can show without rounding noise.
  var STOPS = [0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.25, 1.5, 2, 2.5, 3, 4, 5];

  var webview = window.chrome && window.chrome.webview ? window.chrome.webview : null;

  var els = {
    surface: document.getElementById('surface'),
    canvas: document.getElementById('canvas'),
    level: document.getElementById('zoom-level'),
    out: document.getElementById('zoom-out'),
    in: document.getElementById('zoom-in'),
    fit: document.getElementById('zoom-fit'),
    reset: document.getElementById('zoom-reset'),
    center: document.getElementById('zoom-center'),
    gone: document.getElementById('gone'),
    invalid: document.getElementById('invalid'),
    hint: document.getElementById('hint')
  };

  var svg = null;

  // The same diagram drawn light, for everything that leaves this window. See setDiagram.
  var output = null;
  var natural = { width: 0, height: 0 };
  var zoom = 1;

  // Whether the zoom is still the one fit() chose. See the resize observer below.
  var fitted = false;

  function post(type, payload) {
    if (!webview) { return; }
    try {
      webview.postMessage(JSON.stringify({ type: type, payload: payload || {} }));
    } catch (err) {
      /* The host has gone away; nothing useful to do from here. */
    }
  }

  // ------------------------------------------------------------------- zoom

  /// Held to the toolbar's range, or to a lower floor when one is given: see FIT_MIN.
  function clamp(value, floor) {
    return Math.min(MAX, Math.max(floor || MIN, value));
  }

  function apply() {
    if (!svg) { return; }

    // Explicit pixel dimensions rather than a transform: the SVG re-renders its geometry at
    // the new size, so it stays sharp all the way up.
    svg.style.width = (natural.width * zoom) + 'px';
    svg.style.height = (natural.height * zoom) + 'px';

    els.level.textContent = Math.round(zoom * 100) + '%';
    els.out.disabled = zoom <= MIN + 0.0001;
    els.in.disabled = zoom >= MAX - 0.0001;
  }

  function setZoom(value, floor) {
    zoom = clamp(value, floor);
    apply();
  }

  /// 100%, chosen by the user, which ends fit mode like any other zoom they pick.
  function actualSize() {
    fitted = false;
    setZoom(1);
  }

  function step(direction) {
    var i;

    // Already at or below the floor, which only a fit can reach: zooming out has nowhere to
    // go, and falling through to setZoom(MIN) would zoom in instead.
    if (direction < 0 && zoom <= MIN + 0.0001) { return; }

    fitted = false;

    if (direction > 0) {
      for (i = 0; i < STOPS.length; i++) {
        if (STOPS[i] > zoom + 0.0001) { setZoom(STOPS[i]); return; }
      }
      setZoom(MAX);
      return;
    }

    for (i = STOPS.length - 1; i >= 0; i--) {
      if (STOPS[i] < zoom - 0.0001) { setZoom(STOPS[i]); return; }
    }

    setZoom(MIN);
  }

  /*
    Puts the middle of the diagram in the middle of the window, at whatever the zoom is.

    Zooming in leaves the scroll where it was, so a few steps up from a large diagram can
    leave you looking at a corner with no quick way back. This is that way back, and it
    deliberately does not touch the zoom.

    A diagram smaller than the window has nothing to scroll, so both of these come out at
    zero and the canvas is already centered by its own margins.
  */
  function center() {
    els.surface.scrollLeft = (els.surface.scrollWidth - els.surface.clientWidth) / 2;
    els.surface.scrollTop = (els.surface.scrollHeight - els.surface.clientHeight) / 2;
  }

  /*
    Largest zoom at which the whole diagram still fits, enlarging as readily as shrinking.

    It used to cap at 100%, which read as "never blow a small diagram up". That cap is what
    made a maximized window look broken: the window filled the screen and the diagram stayed
    the size it always was, marooned in the middle of it. Enlarging costs nothing here
    because zoom sets the SVG's pixel dimensions rather than transforming it, so the geometry
    is re-rendered at the new size and stays sharp all the way up. The result is held between
    FIT_MIN and the 500% ceiling, not at the buttons' 25% floor, so a very wide diagram
    still fits.

    The padding is the canvas's 16 on each side and two more for sub-pixel rounding. Without
    those two, a diagram that fits exactly can overflow by a fraction of a pixel. The scrollbar
    that appears takes height, which can add the second scrollbar, which takes width.
  */
  function fit() {
    if (!svg || !natural.width || !natural.height) { return; }

    var padding = 34;
    var available = els.surface.getBoundingClientRect();

    fitted = true;
    setZoom(Math.min(
      (available.width - padding) / natural.width,
      (available.height - padding) / natural.height), FIT_MIN);
  }

  /*
    Fit mode: a window keeps its diagram fitted until the user picks a zoom of their own.

    A window opens fitted, and the fit has to survive the window settling. The page loads
    while the host is still sizing and activating the window, so the surface measured by the
    first fit can be a size the window is about to stop being. A diagram fitted to that size
    came up visibly too wide. The same is true of a maximize the host asks for: the message
    arrives before the WebView has been laid out at its new size.

    So every resize refits for as long as the zoom is the fitted one. Zooming in or out, or
    asking for 100%, ends it; Fit to Window starts it again. Centering and panning leave it
    alone, because neither changes the zoom.
  */
  if (window.ResizeObserver) {
    new ResizeObserver(function () {
      if (fitted) { fit(); }
    }).observe(els.surface);
  }

  // ---------------------------------------------------------------- content

  /// Natural size from the viewBox, falling back to whatever the element reports.
  function measure(element) {
    var box = element.viewBox && element.viewBox.baseVal;

    if (box && box.width > 0 && box.height > 0) {
      return { width: box.width, height: box.height };
    }

    var rect = element.getBoundingClientRect();
    return { width: rect.width || 800, height: rect.height || 600 };
  }

  /*
    `outputMarkup` is the light drawing of the same diagram. Dark mode is a screen setting, so
    while the window is dark it carries both: the dark one to look at, and the light one held
    beside it for diagram.css to print in its place and for Copy as PNG to rasterize. While
    the window is light the two are the same markup, and only one is kept.
  */
  function setDiagram(markup, outputMarkup) {
    // The first diagram is fitted to the window. Later ones are edits of the one on screen:
    // still in fit mode they are fitted too, so a diagram that grows stays whole, but once
    // the user has picked a zoom, refitting would yank it out from under them mid-read.
    var isFirst = svg === null;

    var scroll = { left: els.surface.scrollLeft, top: els.surface.scrollTop };

    els.canvas.innerHTML = markup;
    svg = els.canvas.querySelector('svg');

    if (!svg) {
      els.canvas.innerHTML = '<p class="mq-error">This diagram could not be displayed.</p>';
      return;
    }

    output = outputMarkup || markup;

    if (output !== markup) {
      var copy = document.createElement('div');
      copy.className = 'mq-output-copy';
      copy.setAttribute('aria-hidden', 'true');
      copy.innerHTML = output;
      els.canvas.appendChild(copy);
    }

    natural = measure(svg);

    // Mermaid caps its output with a max-width so it behaves inside a document column. Here
    // the diagram owns the window, and that cap would silently defeat zooming in.
    svg.style.maxWidth = 'none';
    svg.removeAttribute('width');
    svg.removeAttribute('height');

    if (isFirst || fitted) {
      fit();
      return;
    }

    apply();

    // Holding the scroll position keeps the part being edited roughly where it was. It is
    // approximate by nature: the diagram just changed shape underneath it.
    els.surface.scrollLeft = scroll.left;
    els.surface.scrollTop = scroll.top;
  }

  /*
    Notices about the source, shown in place of the usage hint.

    Both are held here and drawn from one place rather than each toggling the hint on its
    own, because whichever was cleared last would otherwise decide whether the hint comes
    back and the other notice would lose its slot.
  */
  var notice = { gone: false, invalid: null };

  function drawNotice() {
    els.gone.hidden = !notice.gone;
    els.invalid.hidden = !notice.invalid;
    els.invalid.title = notice.invalid || '';
    els.hint.hidden = notice.gone || !!notice.invalid;
  }

  /// The diagram is no longer in its document.
  function setRemoved(removed) {
    notice.gone = !!removed;
    drawNotice();
  }

  /// The definition does not parse; what is on screen is the last render that did.
  function setInvalid(message) {
    notice.invalid = message || null;
    drawNotice();
  }

  /*
    Rasterizes the light drawing and hands the PNG to the host, which owns the clipboard.

    The rasterizer is shared with the preview shell - see diagram-raster.js, which carries
    the two traps worth knowing about - so both Copy as PNG items produce the same picture.
  */
  function copyPng() {
    if (!output) { return; }

    window.mqDiagramRaster.markupToPngBase64(output, 2, window.mqDiagramRaster.copyMargin).then(function (data) {
      post('diagramPng', { data: data });
    }).catch(function (err) {
      post('diagramPngError', { message: err.message });
    });
  }

  /*
    Names the file the diagram came from, in the page title.

    Nothing of it reaches paper: a printed page carries the diagram and nothing else. The
    title is set because it is what the print queue lists the job under.
  */
  function setSource(path) {
    document.title = path ? ('Marqora - ' + path) : 'Marqora - Diagram';
  }

  function setTheme(theme) {
    document.documentElement.setAttribute('data-theme', theme === 'Dark' ? 'dark' : 'light');
  }

  // ----------------------------------------------------------------- events

  els.in.addEventListener('click', function () { step(1); });
  els.out.addEventListener('click', function () { step(-1); });
  els.fit.addEventListener('click', fit);
  els.reset.addEventListener('click', actualSize);
  els.center.addEventListener('click', center);

  els.surface.addEventListener('wheel', function (e) {
    if (!e.ctrlKey) { return; }
    e.preventDefault();
    step(e.deltaY < 0 ? 1 : -1);
  }, { passive: false });

  // ------------------------------------------------------------------- panning

  /*
    Hold the middle button and drag to pan.

    A zoomed diagram is usually wider than the window rather than taller, and a wheel alone
    cannot move sideways, so this is the only comfortable way across a large diagram.

    The cursor names the axes that can actually move: a diagram that only overflows
    horizontally gets the left/right cursor, and one that overflows both gets all-scroll.
    Showing a direction the diagram cannot travel in is worse than showing none.
  */
  var panning = null;

  function scrollableAxes() {
    return {
      // The slack allows for sub-pixel layout, which otherwise reports a permanent 1px
      // overflow and would offer to pan a diagram that visibly fits.
      horizontal: els.surface.scrollWidth > els.surface.clientWidth + 1,
      vertical: els.surface.scrollHeight > els.surface.clientHeight + 1
    };
  }

  function panCursor(axes) {
    if (axes.horizontal && axes.vertical) { return 'all-scroll'; }
    if (axes.horizontal) { return 'ew-resize'; }
    return 'ns-resize';
  }

  els.surface.addEventListener('mousedown', function (e) {
    if (e.button !== 1) { return; }

    var axes = scrollableAxes();
    if (!axes.horizontal && !axes.vertical) { return; }

    // Also suppresses the browser's own middle-click autoscroll, so there is one pan
    // behavior in this window rather than two fighting over the same button.
    e.preventDefault();

    panning = {
      x: e.clientX,
      y: e.clientY,
      left: els.surface.scrollLeft,
      top: els.surface.scrollTop,
      axes: axes
    };

    document.body.style.setProperty('--mq-pan-cursor', panCursor(axes));
    document.body.classList.add('mq-panning');
  });

  // On window rather than the surface: the pointer routinely leaves the surface mid-drag,
  // and a pan that stopped at the edge would be worse than useless.
  window.addEventListener('mousemove', function (e) {
    if (!panning) { return; }

    if (panning.axes.horizontal) {
      els.surface.scrollLeft = panning.left - (e.clientX - panning.x);
    }

    if (panning.axes.vertical) {
      els.surface.scrollTop = panning.top - (e.clientY - panning.y);
    }
  });

  function endPan() {
    if (!panning) { return; }

    panning = null;
    document.body.classList.remove('mq-panning');
    document.body.style.removeProperty('--mq-pan-cursor');
  }

  window.addEventListener('mouseup', function (e) { if (e.button === 1) { endPan(); } });

  // Releasing the button outside the window never reports a mouseup here, which would
  // otherwise leave the page stuck in a pan.
  window.addEventListener('blur', endPan);

  // Nothing should follow a middle click; without this the browser may still act on it.
  els.surface.addEventListener('auxclick', function (e) {
    if (e.button === 1) { e.preventDefault(); }
  });

  window.addEventListener('keydown', function (e) {
    if (!e.ctrlKey) { return; }

    if (e.key === '0') { e.preventDefault(); actualSize(); }
    else if (e.key === '+' || e.key === '=') { e.preventDefault(); step(1); }
    else if (e.key === '-') { e.preventDefault(); step(-1); }
  });

  /*
    Right-click, reported to the host so it can put a native menu up.

    Chromium's own menu is switched off in DiagramWindow, for the same reason it is off in
    the main window: it was drawn by Edge, so it followed Edge's dark mode rather than the
    app's theme, and it offered browser commands a diagram has no use for. The host shows a
    WinUI flyout carrying this window's own commands instead.
  */
  window.addEventListener('contextmenu', function (e) {
    e.preventDefault();
    post('contextMenu', { x: Math.round(e.clientX), y: Math.round(e.clientY) });
  });

  /*
    The menu's commands, which are the toolbar's commands. Named here rather than reached
    for by simulating a button click, so the two routes cannot drift apart.
  */
  function runCommand(name) {
    if (name === 'zoomIn') { step(1); }
    else if (name === 'zoomOut') { step(-1); }
    else if (name === 'zoomReset') { actualSize(); }
    else if (name === 'zoomFit') { fit(); }
    else if (name === 'center') { center(); }
    else if (name === 'copyPng') { copyPng(); }
  }

  if (webview) {
    webview.addEventListener('message', function (e) {
      var message = e.data;

      if (typeof message === 'string') {
        try { message = JSON.parse(message); } catch (err) { return; }
      }

      if (!message || !message.type) { return; }

      if (message.type === 'setDiagram') { setDiagram(message.payload.svg, message.payload.outputSvg); }
      else if (message.type === 'setTheme') { setTheme(message.payload.theme); }
      else if (message.type === 'setRemoved') { setRemoved(message.payload.removed); }
      else if (message.type === 'setInvalid') { setInvalid(message.payload.message); }
      else if (message.type === 'setSource') { setSource(message.payload.path); }
      else if (message.type === 'command') { runCommand(message.payload.name); }
    });
  }

  post('ready', {});
}());
