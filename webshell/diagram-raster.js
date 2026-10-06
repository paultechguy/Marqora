/*
  Turning a rendered diagram into PNG bytes.

  Shared by the preview shell and the diagram pop-out, because both offer "Copy as PNG" and
  a second copy of this would be a second set of the traps below. Neither page writes to the
  clipboard itself - a browser only honors a copy during a trusted user gesture, and a click
  on a native menu is not one - so both hand the bytes to the host, which owns the clipboard.

  Two things here are load-bearing and look arbitrary:

  The serialized SVG is loaded from a data: URL, not a blob: URL. Mermaid's labels are HTML
  inside <foreignObject>, and Chromium refuses to export a canvas that has drawn an SVG
  image containing one - unless the image came from a data: URL, which the canvas taint
  check waves through before it ever looks inside. A blob: URL takes the path that does
  look, and the export dies with "Tainted canvases may not be exported".

  That image renders in an isolated document which cannot fetch anything, so the labels keep
  their metrics only because the diagram font is an installed system font. A web font would
  fall back to Arial in the export and overflow the boxes Mermaid sized for it.
*/

(function () {
  'use strict';

  /// The whole coordinate space of the SVG, which is what it is asked to draw into.
  function viewBoxOf(element) {
    var box = element.viewBox && element.viewBox.baseVal;

    if (box && box.width > 0 && box.height > 0) {
      return { x: box.x, y: box.y, width: box.width, height: box.height };
    }

    var rect = element.getBoundingClientRect();
    return { x: 0, y: 0, width: rect.width || 800, height: rect.height || 600 };
  }

  /*
    What the diagram actually occupies, which is smaller than the space it was given.

    Mermaid sets a viewBox with diagramPadding around the drawing, so rasterizing the
    viewBox leaves an empty margin on all four sides - visible as a band in whatever the
    pasting application paints for empty pixels. getBBox measures the drawn geometry
    instead, so the picture ends where the diagram does.

    Inflated a little because getBBox measures paths and ignores how thickly they are
    stroked: a node's border is centered on its path, so half its width lies outside the box
    and a tight crop would shave it.

    Falls back to the viewBox when there is nothing measurable - an SVG that has not been
    laid out has no bounds to report, and the whole canvas is the honest answer then.
  */
  function drawnBounds(element) {
    var bleed = 2;

    try {
      var box = element.getBBox();

      if (box.width > 0 && box.height > 0) {
        return {
          x: box.x - bleed,
          y: box.y - bleed,
          width: box.width + (bleed * 2),
          height: box.height + (bleed * 2)
        };
      }
    } catch (err) {
      /* Not laid out, or nothing drawn. The viewBox below covers both. */
    }

    return viewBoxOf(element);
  }

  /// The bounds grown by the same amount on every side.
  function withMargin(bounds, margin) {
    return {
      x: bounds.x - margin,
      y: bounds.y - margin,
      width: bounds.width + (margin * 2),
      height: bounds.height + (margin * 2)
    };
  }

  /*
    Resolves with the PNG as base64, ready to cross the bridge; rejects with something the
    host can put in the log.

    Fixed scale rather than whatever zoom the diagram is being shown at: a copy should be
    crisp regardless of the state of the window it was taken from. The clone is stripped of
    its inline style before width and height are set, because the live element carries a
    zoom-scaled px size in style that would otherwise win over the attributes.

    Measured from the live element rather than the clone: bounds come from layout, and the
    clone is never in a document to be laid out.

    `margin` is empty space added on every side, in the diagram's own units, so it grows with
    the scale the way the drawing does. The two Copy as PNG items ask for it, so a pasted
    diagram's outermost shapes do not sit against the picture's edge. The Word export and
    rich-text copy do not: the document already sets a picture apart from its text, and a
    margin there would be a second one. It is transparent like the rest of the background,
    and the host decides what goes behind it.
  */
  function toPngBase64(svgElement, scale, margin) {
    return new Promise(function (resolve, reject) {
      var bounds = withMargin(drawnBounds(svgElement), margin || 0);
      var width = Math.round(bounds.width * scale);
      var height = Math.round(bounds.height * scale);

      // The crop is the viewBox: the clone is told to draw that region, at that size, and
      // the canvas is the same region again at the export scale.
      var clone = svgElement.cloneNode(true);
      clone.removeAttribute('style');
      clone.setAttribute(
        'viewBox', bounds.x + ' ' + bounds.y + ' ' + bounds.width + ' ' + bounds.height);
      clone.setAttribute('width', bounds.width);
      clone.setAttribute('height', bounds.height);

      var xml = new XMLSerializer().serializeToString(clone);
      var img = new Image();

      img.onload = function () {
        try {
          var canvas = document.createElement('canvas');
          canvas.width = width;
          canvas.height = height;

          // Nothing is painted behind the diagram: what mermaid drew is all this carries,
          // which is what "Copy as SVG" copies too. The canvas starts transparent, and the
          // host decides what goes behind it - see ClipboardImage.SetAsync, which puts the
          // clipboard's one picture onto white.
          var ctx = canvas.getContext('2d');

          // Drawing an SVG-backed image at a larger target size re-rasterizes the vector at
          // that size rather than stretching a bitmap, which is the whole point of the scale.
          ctx.drawImage(img, 0, 0, width, height);

          canvas.toBlob(function (blob) {
            if (!blob) {
              reject(new Error('The canvas produced no PNG data.'));
              return;
            }

            var reader = new FileReader();

            reader.onerror = function () {
              reject(new Error('The PNG could not be read back.'));
            };

            reader.onloadend = function () {
              var result = String(reader.result || '');
              var data = result.substring(result.indexOf(',') + 1);

              if (data) {
                resolve(data);
              } else {
                reject(new Error('The PNG could not be read back.'));
              }
            };

            reader.readAsDataURL(blob);
          }, 'image/png');
        } catch (err) {
          reject(err);
        }
      };

      img.onerror = function () {
        reject(new Error('The diagram could not be decoded as an image.'));
      };

      img.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(xml);
    });
  }

  /*
    The same, from markup rather than an element on screen.

    The light drawing of a diagram is hidden on screen, and a hidden element has no layout
    for drawnBounds to measure. So it is set down off-screen for as long as the rasterizing
    takes and taken away again. Mermaid scopes its styles by the SVG's own id, so the copy
    restyles nothing else on the page while it is there.
  */
  function markupToPngBase64(markup, scale, margin) {
    var stage = document.createElement('div');
    stage.setAttribute('aria-hidden', 'true');
    stage.style.cssText = 'position:absolute;left:-99999px;top:0;';
    stage.innerHTML = markup;
    document.body.appendChild(stage);

    var svg = stage.querySelector('svg');

    var done = svg
      ? toPngBase64(svg, scale, margin)
      : Promise.reject(new Error('The diagram markup carried no SVG.'));

    return done.finally(function () { stage.remove(); });
  }

  /*
    The margin both Copy as PNG items ask for, held here so the preview's copy and the
    pop-out's cannot drift apart. In the diagram's units, so a copy at twice the size carries
    twice the pixels.
  */
  var COPY_MARGIN = 16;

  window.mqDiagramRaster = {
    toPngBase64: toPngBase64,
    markupToPngBase64: markupToPngBase64,
    copyMargin: COPY_MARGIN
  };
}());
