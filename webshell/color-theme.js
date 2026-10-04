/*
  The document's color theme, as a stylesheet.

  A theme is a palette per mode - slot to #rrggbb - chosen in the app (PaulTechGuy.MQ.Themes)
  and posted here. app.css reads each slot as var(--mq-theme-<slot>) and declares none of them;
  this writes them all, into one <style> element of its own:

    :root                              { the light palette }
    :root[data-theme="dark"]           { the dark palette }
    @media print { :root, [dark] ...   { the light palette } }

  The same three blocks app.css uses for its own colors, and keyed the same way, for two
  reasons. A mode switch then needs nothing from here: data-theme flips and the right palette
  is already in force. And the clipboard copy, which inlines computed styles after briefly
  setting data-theme to light, gets the light palette for free - a block holding only "the
  current mode" would hand it the dark one.

  A stylesheet rather than style.setProperty on the root element, which is how the accent
  arrives. An inline property outranks every rule, so a print block could never re-point it;
  the accent copes with a pair of properties and a mapping line in each block, and with sixty
  odd slots that would put the whole slot list into app.css twice.

  Shared by the preview shell and the cheatsheet, the way diagram-output.js is.
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

  function stylesheet(light, dark) {
    var day = declarations(light);

    return ':root {\n' + day + '\n}\n'
      + ':root[data-theme="dark"] {\n' + declarations(dark) + '\n}\n'
      + '@media print {\n:root,\n:root[data-theme="dark"] {\n' + day + '\n}\n}\n';
  }

  /*
    Puts a theme in force: { light: {...}, dark: {...} }. Remembers the last one applied, so
    asking again for the theme already showing - every tab switch between two documents that
    wear the same one - costs a comparison and nothing more.
  */
  var current = null;

  function apply(theme, key) {
    if (!theme || !theme.light || !theme.dark) { return; }
    if (key && key === current) { return; }

    var style = document.getElementById(STYLE_ID);

    if (!style) {
      style = document.createElement('style');
      style.id = STYLE_ID;
      document.head.appendChild(style);
    }

    style.textContent = stylesheet(theme.light, theme.dark);
    current = key || null;
  }

  window.mqColorTheme = { apply: apply };
}());
