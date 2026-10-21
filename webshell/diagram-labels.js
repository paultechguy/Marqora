/*
  Pulling apart flowchart edge labels that mermaid drew on top of each other.

  Mermaid's flowchart layout routes every edge first and then puts the edge's label at the
  midpoint of its path. It never asks whether another label is already there, so two edges
  that pass close together near their middles get their labels stacked: a short "schema"
  printed over the step number at the start of a longer label, and neither readable.

  This runs over the finished drawing and slides a label that overlaps another along its own
  edge until it is clear - nearest clear spot first, never past the nodes at either end. It
  needs no measuring: mermaid writes each label's position and size into the SVG, and tags the
  label and its path with the same data-id (checked against mermaid 11.17), so everything here
  is arithmetic on attributes and the result is the same every time for the same drawing. A
  mermaid that writes its labels differently finds nothing to move. A label with no clear spot
  stays where mermaid put it, so a diagram is never worse for having been through here.

  Only the drawing changes. The definition is untouched, and so is the document - the preview
  never edits the source.

  Flowcharts only: that is the layout with the midpoint rule. Labels that sit on an unrelated
  edge are not touched either; that needs every path in the drawing as an obstacle, not just
  the other labels and the nodes.

  Shared by the preview shell and the cheatsheet. Both drawings of a diagram - the one on screen
  and the light one that leaves the app - come through here, so a PDF shows what the screen
  shows.
*/

(function () {
  'use strict';

  // Clear space kept between a moved label and whatever it was moved away from.
  var GAP = 3;

  // How far apart the spots tried along an edge are.
  var STEP = 4;

  // The share of an edge at each end a label is not moved into.
  var END_MARGIN = 0.1;

  // Points per curve segment when a path is flattened into a line to walk along.
  var CURVE_STEPS = 16;

  // By the drawing's markup. A redraw of an unchanged diagram hands back the same string, so
  // every keystroke that does not touch the diagram is a lookup.
  var cache = new Map();
  var CACHE_LIMIT = 200;

  /*
    The markup of the drawing with its overlapping edge labels moved apart, or the markup it
    was given when there was nothing to move - which is most drawings, and costs a parse.
  */
  function separate(svg) {
    if (typeof svg !== 'string' || svg.indexOf('edgeLabel') < 0) { return svg; }

    if (cache.has(svg)) {
      var hit = cache.get(svg);
      cache.delete(svg);
      cache.set(svg, hit);
      return hit;
    }

    var result = svg;

    try {
      result = separateMarkup(svg);
    } catch (err) {
      // A drawing this does not understand is shown as mermaid drew it.
      result = svg;
    }

    cache.set(svg, result);

    if (cache.size > CACHE_LIMIT) {
      cache.delete(cache.keys().next().value);
    }

    return result;
  }

  function separateMarkup(svg) {
    // A template parses without running or loading anything, and nothing here needs layout.
    var template = document.createElement('template');
    template.innerHTML = svg;

    var root = template.content.querySelector('svg');
    if (!root || !/^flowchart/.test(root.getAttribute('aria-roledescription') || '')) { return svg; }

    var labels = edgeLabels(root);
    if (labels.length < 2) { return svg; }

    var obstacles = nodeBoxes(root);
    var paths = edgePaths(root);
    var moved = false;

    for (var i = 0; i < labels.length; i++) {
      var label = labels[i];
      if (!overlapsAny(label.box, labels, label)) { continue; }

      var route = paths[label.id];
      if (!route) { continue; }

      var spot = clearSpot(label, route, labels, obstacles);
      if (!spot) { continue; }

      label.cx = spot.x;
      label.cy = spot.y;
      label.box = boxAt(label, spot.x, spot.y);
      label.group.setAttribute('transform', 'translate(' + round(spot.x) + ', ' + round(spot.y) + ')');
      moved = true;
    }

    return moved ? template.innerHTML : svg;
  }

  /*
    Every edge label with something in it: where its center sits, and the box it covers.

    The outer g.edgeLabel carries the center as a translate; the inner g.label shifts its
    content up and left by half its size, and the content is a foreignObject (HTML labels,
    the default) or a background rect (SVG labels) with the size written on it. An edge with
    no label still gets an empty g.edgeLabel, which is skipped.
  */
  function edgeLabels(root) {
    var groups = root.querySelectorAll('g.edgeLabel');
    var labels = [];

    for (var i = 0; i < groups.length; i++) {
      var group = groups[i];
      var inner = group.querySelector(':scope > g.label[data-id]');
      if (!inner) { continue; }

      var sized = inner.querySelector(':scope > foreignObject, :scope > rect');
      if (!sized) { continue; }

      var width = parseFloat(sized.getAttribute('width'));
      var height = parseFloat(sized.getAttribute('height'));
      if (!(width >= 1 && height >= 1)) { continue; }

      var center = translateOf(group);
      var shift = translateOf(inner);
      if (!center || !shift) { continue; }

      var label = {
        id: inner.getAttribute('data-id'),
        group: group,
        cx: center.x,
        cy: center.y,
        left: shift.x + (parseFloat(sized.getAttribute('x')) || 0),
        top: shift.y + (parseFloat(sized.getAttribute('y')) || 0),
        width: width,
        height: height
      };

      label.box = boxAt(label, label.cx, label.cy);
      labels.push(label);
    }

    return labels;
  }

  function boxAt(label, cx, cy) {
    var x = cx + label.left;
    var y = cy + label.top;
    return { x1: x, y1: y, x2: x + label.width, y2: y + label.height };
  }

  /*
    The boxes of the nodes, which a moved label must not land on.

    From the node's own shape where it has a plain one - a rect, a polygon for the diamond and
    the hexagon, a circle - and otherwise from its label with some padding, which covers the
    shapes mermaid draws as a path.
  */
  function nodeBoxes(root) {
    var nodes = root.querySelectorAll('g.node');
    var boxes = [];

    for (var i = 0; i < nodes.length; i++) {
      var at = translateOf(nodes[i]);
      if (!at) { continue; }

      var box = shapeBox(nodes[i]);
      if (box) {
        boxes.push({ x1: at.x + box.x1, y1: at.y + box.y1, x2: at.x + box.x2, y2: at.y + box.y2 });
      }
    }

    return boxes;
  }

  function shapeBox(node) {
    var rect = node.querySelector(':scope > rect');
    if (rect && rect.hasAttribute('width')) {
      var x = parseFloat(rect.getAttribute('x')) || 0;
      var y = parseFloat(rect.getAttribute('y')) || 0;
      return { x1: x, y1: y, x2: x + parseFloat(rect.getAttribute('width')), y2: y + parseFloat(rect.getAttribute('height')) };
    }

    var polygon = node.querySelector(':scope > polygon');
    if (polygon) {
      var numbers = numbersIn(polygon.getAttribute('points'));
      var shift = translateOf(polygon) || { x: 0, y: 0 };
      if (numbers.length >= 4) { return extent(numbers, shift); }
    }

    var circle = node.querySelector(':scope > circle');
    if (circle) {
      var r = parseFloat(circle.getAttribute('r')) || 0;
      return { x1: -r, y1: -r, x2: r, y2: r };
    }

    var text = node.querySelector(':scope > g.label foreignObject');
    var label = text && translateOf(text.parentNode);
    if (label) {
      var pad = 16;
      return {
        x1: label.x - pad,
        y1: label.y - pad,
        x2: label.x + parseFloat(text.getAttribute('width')) + pad,
        y2: label.y + parseFloat(text.getAttribute('height')) + pad
      };
    }

    return null;
  }

  function extent(numbers, shift) {
    var box = { x1: Infinity, y1: Infinity, x2: -Infinity, y2: -Infinity };

    for (var i = 0; i + 1 < numbers.length; i += 2) {
      box.x1 = Math.min(box.x1, numbers[i] + shift.x);
      box.x2 = Math.max(box.x2, numbers[i] + shift.x);
      box.y1 = Math.min(box.y1, numbers[i + 1] + shift.y);
      box.y2 = Math.max(box.y2, numbers[i + 1] + shift.y);
    }

    return box;
  }

  /*
    Each edge's path as a line to walk along, by the id its label carries.

    From the drawn path where its commands are the absolute ones d3 writes, so a label moved
    along a curve stays on the curve. Otherwise from data-points, the route the layout chose,
    which the curve follows closely enough.
  */
  function edgePaths(root) {
    var found = root.querySelectorAll('path[data-edge][data-id]');
    var paths = {};

    for (var i = 0; i < found.length; i++) {
      var id = found[i].getAttribute('data-id');
      var points = flattenPath(found[i].getAttribute('d')) || routePoints(found[i].getAttribute('data-points'));

      if (points && points.length >= 2) { paths[id] = measured(points); }
    }

    return paths;
  }

  function flattenPath(d) {
    if (!d || /[a-z]/.test(d.replace(/e[-+]?\d/gi, ''))) { return null; }

    var tokens = d.match(/[MLHVCQZ]|-?(?:\d+\.?\d*|\.\d+)(?:e[-+]?\d+)?/gi);
    if (!tokens) { return null; }

    var points = [];
    var command = null;
    var x = 0;
    var y = 0;
    var start = null;
    var i = 0;

    function next() {
      var value = parseFloat(tokens[i++]);
      if (isNaN(value)) { throw new Error('path'); }
      return value;
    }

    try {
      while (i < tokens.length) {
        if (/^[A-Z]$/.test(tokens[i])) { command = tokens[i++]; }

        switch (command) {
          case 'M':
            x = next(); y = next();
            start = { x: x, y: y };
            points.push({ x: x, y: y });
            command = 'L';
            break;
          case 'L':
            x = next(); y = next();
            points.push({ x: x, y: y });
            break;
          case 'H':
            x = next();
            points.push({ x: x, y: y });
            break;
          case 'V':
            y = next();
            points.push({ x: x, y: y });
            break;
          case 'C':
            var c1x = next(), c1y = next(), c2x = next(), c2y = next(), ex = next(), ey = next();
            for (var s = 1; s <= CURVE_STEPS; s++) {
              var t = s / CURVE_STEPS, u = 1 - t;
              points.push({
                x: u * u * u * x + 3 * u * u * t * c1x + 3 * u * t * t * c2x + t * t * t * ex,
                y: u * u * u * y + 3 * u * u * t * c1y + 3 * u * t * t * c2y + t * t * t * ey
              });
            }
            x = ex; y = ey;
            break;
          case 'Q':
            var qx = next(), qy = next(), qex = next(), qey = next();
            for (var q = 1; q <= CURVE_STEPS; q++) {
              var tq = q / CURVE_STEPS, uq = 1 - tq;
              points.push({
                x: uq * uq * x + 2 * uq * tq * qx + tq * tq * qex,
                y: uq * uq * y + 2 * uq * tq * qy + tq * tq * qey
              });
            }
            x = qex; y = qey;
            break;
          case 'Z':
            if (start) { points.push({ x: start.x, y: start.y }); x = start.x; y = start.y; }
            command = null;
            break;
          default:
            return null;
        }
      }
    } catch (err) {
      return null;
    }

    return points;
  }

  function routePoints(encoded) {
    if (!encoded) { return null; }

    try {
      var points = JSON.parse(atob(encoded));
      return Array.isArray(points) ? points.filter(function (p) {
        return p && isFinite(p.x) && isFinite(p.y);
      }) : null;
    } catch (err) {
      return null;
    }
  }

  // The points with the distance along the line to each, for walking it by length.
  function measured(points) {
    var lengths = [0];

    for (var i = 1; i < points.length; i++) {
      lengths.push(lengths[i - 1] + Math.hypot(points[i].x - points[i - 1].x, points[i].y - points[i - 1].y));
    }

    return { points: points, lengths: lengths, total: lengths[lengths.length - 1] };
  }

  function pointAt(route, distance) {
    var lengths = route.lengths;
    var i = 1;

    while (i < lengths.length - 1 && lengths[i] < distance) { i++; }

    var span = lengths[i] - lengths[i - 1];
    var t = span > 0 ? (distance - lengths[i - 1]) / span : 0;
    var a = route.points[i - 1];
    var b = route.points[i];

    return { x: a.x + (b.x - a.x) * t, y: a.y + (b.y - a.y) * t };
  }

  // How far along the line the point nearest the label's center is.
  function nearestDistance(route, x, y) {
    var best = 0;
    var bestGap = Infinity;

    for (var d = 0; d <= route.total; d += STEP / 2) {
      var p = pointAt(route, d);
      var gap = Math.hypot(p.x - x, p.y - y);
      if (gap < bestGap) { bestGap = gap; best = d; }
    }

    return best;
  }

  /*
    The nearest spot along the label's own edge where it touches no other label and no node.

    Spots are tried outward from where mermaid put it, one step either way at a time, so the
    label moves as little as it can. Whatever offset mermaid gave it from the line - a subgraph
    title margin - goes with it.
  */
  function clearSpot(label, route, labels, obstacles) {
    var home = nearestDistance(route, label.cx, label.cy);
    var anchor = pointAt(route, home);
    var dx = label.cx - anchor.x;
    var dy = label.cy - anchor.y;

    // Not the last tenth at either end, where the arrowhead and the node's edge are.
    var low = route.total * END_MARGIN;
    var high = route.total * (1 - END_MARGIN);

    for (var k = 1; k * STEP <= route.total; k++) {
      for (var side = -1; side <= 1; side += 2) {
        var distance = home + side * k * STEP;
        if (distance < low || distance > high) { continue; }

        var p = pointAt(route, distance);
        var x = p.x + dx;
        var y = p.y + dy;
        var box = boxAt(label, x, y);

        if (!overlapsAny(box, labels, label) && !hitsAny(box, obstacles)) {
          return { x: x, y: y };
        }
      }
    }

    return null;
  }

  function overlapsAny(box, labels, self) {
    for (var i = 0; i < labels.length; i++) {
      if (labels[i] !== self && overlaps(box, labels[i].box)) { return true; }
    }

    return false;
  }

  function hitsAny(box, boxes) {
    for (var i = 0; i < boxes.length; i++) {
      if (overlaps(box, boxes[i])) { return true; }
    }

    return false;
  }

  function overlaps(a, b) {
    return a.x1 < b.x2 + GAP && b.x1 < a.x2 + GAP && a.y1 < b.y2 + GAP && b.y1 < a.y2 + GAP;
  }

  function translateOf(element) {
    var match = /translate\(\s*(-?[\d.e+-]+)[\s,]+(-?[\d.e+-]+)\s*\)/i.exec(element.getAttribute('transform') || '');
    return match ? { x: parseFloat(match[1]), y: parseFloat(match[2]) } : null;
  }

  function numbersIn(text) {
    return (text || '').split(/[\s,]+/).filter(Boolean).map(parseFloat);
  }

  function round(value) {
    return Math.round(value * 100) / 100;
  }

  window.mqDiagramLabels = { separate: separate };
})();
