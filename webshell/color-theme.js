/*
  The document's color theme, as a stylesheet.

  A theme is a palette per mode - slot to #rrggbb - chosen in the app (PaulTechGuy.MQ.Themes)
  and posted here. app.css reads each slot as var(--mq-theme-<slot>) and declares none of them;
  this writes them all, into one <style> element of its own:

    :root                              { the screen theme's light palette }
    :root[data-theme="dark"]           { the screen theme's dark palette }
    @media print { :root, [dark] ...   { the export theme's light palette } }

  The same three blocks app.css uses for its own colors, and keyed the same way. A mode switch
  then needs nothing from here: data-theme flips and the right palette is already in force.
  Print takes the export theme, which is the screen's unless Preferences names another for
  exports, and always its light palette, because paper is white.

  The clipboard copy is an export too, but it measures computed styles on screen rather than
  printing, after briefly setting data-theme to light. For that moment app.js puts the export
  theme in force on screen as well, so what it measures is what the copy should carry.

  A stylesheet rather than style.setProperty on the root element, which is how the accent
  arrives. An inline property outranks every rule, so a print block could never re-point it;
  the accent copes with a pair of properties and a mapping line in each block, and with sixty
  odd slots that would put the whole slot list into app.css twice.

  Shared by the preview shell and the cheatsheet, the way diagram-output.js is - and so is the
  rewrite that hands a theme's diagram colors to mermaid, themedDefinition.
*/

(function () {
  'use strict';

  var STYLE_ID = 'mq-theme';
  var SLOT = /^[a-z][a-z0-9-]*$/;
  var HEX = /^#[0-9a-f]{6}$/;

  /// One palette as declarations. Anything that is not a slot name and a hex color is skipped.
  function declarations(palette) {
    var lines = [];

    for (var slot in palette) {
      if (!Object.prototype.hasOwnProperty.call(palette, slot)) { continue; }

      var value = String(palette[slot]).toLowerCase();

      if (SLOT.test(slot) && HEX.test(value)) {
        lines.push('  --mq-theme-' + slot + ': ' + value + ';');
      }
    }

    return lines.join('\n');
  }

  function stylesheet(light, dark, paper) {
    return ':root {\n' + declarations(light) + '\n}\n'
      + ':root[data-theme="dark"] {\n' + declarations(dark) + '\n}\n'
      + '@media print {\n:root,\n:root[data-theme="dark"] {\n' + declarations(paper) + '\n}\n}\n';
  }

  /*
    Puts themes in force: `theme` on screen, light and dark, and `print` on paper - the theme
    the user chose for exports, which can differ from the screen's. Each is
    { light: {...}, dark: {...} }, and only the print theme's light palette is read, because
    paper is white. Remembers the last pair applied, so asking again for what is already
    showing costs a comparison and nothing more.
  */
  var current = null;

  function apply(theme, print, key) {
    if (!theme || !theme.light || !theme.dark) { return; }
    if (!print || !print.light) { print = theme; }
    if (key && key === current) { return; }

    var style = document.getElementById(STYLE_ID);

    if (!style) {
      style = document.createElement('style');
      style.id = STYLE_ID;
      document.head.appendChild(style);
    }

    style.textContent = stylesheet(theme.light, theme.dark, print.light);
    current = key || null;
  }

  var BAND_FLOOR = 0.18;
  var BAND_CEILING = 0.20;
  var BAND_LIGHT = 0.35;

  /// WCAG relative luminance of an sRGB color, 0 for black to 1 for white.
  function luminance(r, g, b) {
    function linear(c) {
      c /= 255;
      return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4);
    }

    return (0.2126 * linear(r)) + (0.7152 * linear(g)) + (0.0722 * linear(b));
  }

  /*
    A light color as a deep shade of its own hue, for an author's color chosen for a white page
    that now sits under the dark theme's light ink: the hue and saturation are kept and the
    lightness flipped, with a floor so the color still shows. Null for a color that is already
    dark, which stays as the author wrote it, or for one that cannot be read.

    Shared by the sequence diagram's bands and math's \colorbox, so the two agree.
  */
  function darkShadeOf(color) {
    var rgb = /rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)(?:[,\s/]+([\d.]+))?\s*\)/.exec(color || '');

    if (!rgb) { return null; }

    // Light by how bright it looks, not by HSL lightness: pure yellow is lightness 0.5, the
    // same as pure blue, and one of the brightest colors there is. Relative luminance, as the
    // contrast checks use, tells them apart.
    if (luminance(+rgb[1], +rgb[2], +rgb[3]) <= BAND_LIGHT) { return null; }

    var hsl = toHsl(+rgb[1], +rgb[2], +rgb[3]);
    var alpha = rgb[4] === undefined ? 1 : +rgb[4];

    // A pale color flips to deep; a saturated bright one is held down to the same depth, which
    // is what keeps light ink readable on it.
    var lightness = Math.min(BAND_CEILING, Math.max(BAND_FLOOR, 1 - hsl.l));

    return 'hsla(' + Math.round(hsl.h) + ', ' + Math.round(hsl.s * 100) + '%, '
      + Math.round(lightness * 100) + '%, ' + alpha + ')';
  }

  /*
    The dark-mode shade of each light \colorbox in some freshly typeset math.

    Math is drawn on the page itself, so unlike a diagram there is no second drawing to keep for
    print. The shade is left on the box as a custom property instead, and app.css applies it
    only on screen and only under [data-theme="dark"]: a print, a PDF, an export and Copy as rich
    text - which measures after setting data-theme to light - all keep the author's color. It is
    worked out whatever the mode, so a switch to dark needs nothing from here.
  */
  function shadeMathBackgrounds(root) {
    var boxes = root.querySelectorAll('.katex [style*="background-color"]');

    for (var i = 0; i < boxes.length; i++) {
      var shade = darkShadeOf(getComputedStyle(boxes[i]).backgroundColor);

      if (shade) { boxes[i].style.setProperty('--mq-dark-fill', shade); }
    }
  }

  function toHsl(r, g, b) {
    r /= 255; g /= 255; b /= 255;

    var max = Math.max(r, g, b);
    var min = Math.min(r, g, b);
    var l = (max + min) / 2;
    var d = max - min;

    if (d === 0) { return { h: 0, s: 0, l: l }; }

    var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
    var h = max === r ? (g - b) / d + (g < b ? 6 : 0)
      : max === g ? (b - r) / d + 2
      : (r - g) / d + 4;

    return { h: h * 60, s: s, l: l };
  }

  /// True when the definition picks a mermaid theme of its own, in an init directive or in
  /// frontmatter config. themeVariables alone does not count: that tunes the app's theme.
  function namesOwnTheme(source) {
    var directives = source.match(/%%\{[\s\S]*?\}%%/g) || [];

    for (var i = 0; i < directives.length; i++) {
      if (/\btheme["']?\s*:/.test(directives[i])) { return true; }
    }

    var front = /^\s*---\r?\n([\s\S]*?)\r?\n\s*---/.exec(source);

    return !!front && /^\s*theme\s*:/m.test(front[1]);
  }

  /*
    The definition mermaid draws, in the color theme's diagram colors.

    A theme with diagram colors has them put in front of the author's definition as an init
    line naming mermaid's base theme - the one theme built to be recolored from a handful of
    values, from which it works out the rest (cluster fills, sequence actors, edge labels).
    Each diagram carries its own colors that way, so neither mermaid instance is ever
    re-initialized for a theme: the screen's keeps following light and dark, and the output
    one stays configured light once and never touched, which is the arrangement
    diagram-output.js explains and depends on.

    mermaid merges every init line it finds, later ones winning, so anything else the author
    set - sequence wrapping, curves, fonts - still applies on top. A definition that names a
    mermaid theme of its own is left exactly as written, and so is every definition under a
    theme that keeps mermaid's stock look (Default), whose palettes carry no diagram colors.

    Frontmatter is the one thing that has to stay first, so the line goes after it.

    `palette` is one palette of a theme - its light or its dark - and `mode` says which: 'dark'
    for the screen in dark mode, 'light' for the screen in light mode and for every drawing that
    leaves the app, which is on white. Shared by the preview shell and the cheatsheet, so the two
    draw a theme's diagrams alike.
  */
  var DIAGRAM_VARIABLES = {
    primaryColor: 'diagram-primary',
    primaryBorderColor: 'diagram-primary-border',
    primaryTextColor: 'diagram-primary-text',
    secondaryColor: 'diagram-secondary',
    tertiaryColor: 'diagram-tertiary',
    lineColor: 'diagram-line',
    noteBkgColor: 'diagram-note',
    noteTextColor: 'diagram-note-text',
    // mermaid draws text outside a node - edge labels, sequence messages - in textColor, which
    // it would otherwise take from primaryTextColor anyway; named so the tests' rule that the
    // node text reads on the page as well is visibly the rule that governs it.
    textColor: 'diagram-primary-text'
  };

  function themedDefinition(source, palette, mode) {
    if (!palette || !palette['diagram-primary'] || namesOwnTheme(source)) { return source; }

    var variables = {
      darkMode: mode === 'dark',
      background: mode === 'dark'
        ? (getComputedStyle(document.documentElement).getPropertyValue('--mq-bg').trim() || '#1f1f1f')
        : '#ffffff'
    };

    for (var name in DIAGRAM_VARIABLES) {
      if (Object.prototype.hasOwnProperty.call(DIAGRAM_VARIABLES, name)) {
        variables[name] = palette[DIAGRAM_VARIABLES[name]];
      }
    }

    var line = '%%{init: ' + JSON.stringify({ theme: 'base', themeVariables: variables }) + '}%%\n';
    var front = /^\s*---\r?\n[\s\S]*?\r?\n\s*---[^\n]*(?:\n|$)/.exec(source);

    return front
      ? source.slice(0, front[0].length) + line + source.slice(front[0].length)
      : line + source;
  }

  window.mqColorTheme = {
    apply: apply,
    darkShadeOf: darkShadeOf,
    namesOwnTheme: namesOwnTheme,
    shadeMathBackgrounds: shadeMathBackgrounds,
    themedDefinition: themedDefinition
  };
}());
