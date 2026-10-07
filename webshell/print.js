/*
  The paged print page's side of the conversation with its host.

  The host posts one 'paginate' message carrying the document's print markup, the theme
  stylesheet the preview had in force, the preview root's own properties (the accent arrives
  that way), the page setup and a title. This lays the document out into pages with Paged.js
  and answers 'rendered' with what it measured, or 'failed' with why.

  Week-one spike (docs/Export-Alignment-Plan.md, §4). The furniture - cover page, contents
  with page numbers, running header and page number - follows the same three choices the
  Word export makes, which the host sends; footnotes go to the foot of the page they are
  called on, numbered as the document numbers them.
*/

(function () {
  'use strict';

  /// The sheets the preview uses, in the order shell.html links them. Monaco's is not one:
  /// there is no editor here.
  var SHEETS = ['vendor/katex/katex.min.css', 'app.css', 'syntax.css'];

  /// The faces the paper spec names (§8), and the one it set aside, checked as the page sees
  /// them - S4. A face Office installs as a cloud font is invisible to WebView2.
  var FACES = ['Segoe UI Variable Text', 'Segoe UI', 'Cascadia Mono', 'Aptos'];

  function post(type, data) {
    var message = data || {};

    message.type = type;
    window.chrome.webview.postMessage(message);
  }

  /// A CSS string literal, for the title in the running header.
  function cssString(text) {
    return '"' + String(text || '').replace(/\\/g, '\\\\').replace(/"/g, '\\"').replace(/\n/g, ' ') + '"';
  }

  /*
    The page box, from the dialog's setup. Paged.js turns this into real page elements of
    exactly this size, which is the point: Chromium no longer decides how wide a printed page
    is laid out, so nothing scales the type down on the way to paper.
  */
  function pageSheet(page, title, furniture) {
    var ratio = (page.heightInches - 2 * page.verticalMarginInches)
      / (page.widthInches - 2 * page.horizontalMarginInches);
    var band = 'font: 9pt "Segoe UI", sans-serif; color: #767676;';

    // The running header and page number, when the export asks for them - the same switch
    // as Word's header and footer. The number is each page's own label, written onto the page
    // after layout (labelPages), never Paged.js's page counter: that counter honored a reset
    // for one page only, so the body's page 1 was followed by page 5.
    var boxes = furniture && furniture.headerAndFooter
      ? '  @top-left { content: ' + cssString(title) + '; ' + band + ' }\n'
        + '  @bottom-right { content: var(--mq-page-label, ""); ' + band + ' }\n'
      : '';

    // The page area's shape, which the print block sizes diagrams by. The live print is told it
    // by prepareForPrint; here the page box is known exactly, so it is stated, not inherited
    // from whatever the preview last printed.
    return ':root { --mq-print-page-ratio: ' + ratio.toFixed(4) + '; }\n'
      // A diagram straight after a heading leaves room for the heading, as app.css asks with
      // "h1 + pre.mermaid svg". Paged.js rewrites every "+" rule into an attribute rule of lower
      // specificity, which the diagram's own room then outranks - the flowchart filled its
      // page and left its heading alone on the one before. So the shape is marked in the
      // markup instead (markDiagramsAfterHeadings) and the room asked for by class.
      + '.mq-preview pre.mermaid.mq-after-heading svg { --mq-print-diagram-room: 13em; }\n'
      + '@page {\n'
      + '  size: ' + page.widthInches + 'in ' + page.heightInches + 'in;\n'
      + '  margin: ' + page.verticalMarginInches + 'in ' + page.horizontalMarginInches + 'in;\n'
      + boxes
      + '  @footnote { border-top: 1px solid #d8d8d8; padding-top: 0.4em; margin-top: 0.8em; }\n'
      + '}\n'
      + '@page cover {\n'
      + '  @top-left { content: none; }\n'
      + '  @bottom-right { content: none; }\n'
      + '}\n'
      + furnitureSheet(page);
  }

  /*
    How the cover, the contents and the footnotes look. Values that the paper spec (§8) will
    own once it exists are written here for now, in the faces the spec settled on.

    The cover's block sits a third of the way down the text column, as Word's does
    (DocxFurniture.WriteCoverPage), measured against the column rather than the paper.

    The contents' dot leader is a flex filler, not leader(): Paged.js 0.4.3 has no handler
    for leader(), and an unsupported value would drop the whole content declaration - the
    page number with it. The number is a span filled after layout from the label of the page
    the heading landed on (labelPages), the same label that page's footer shows - so the two
    cannot disagree, which they did by four pages while both came from Paged.js's counters.
  */
  function furnitureSheet(page) {
    var third = ((page.heightInches - 2 * page.verticalMarginInches) / 3).toFixed(3) + 'in';

    return '.mq-cover { page: cover; break-after: page; padding-top: ' + third + '; }\n'
      + '.mq-cover-title { font-size: 28pt; line-height: 1.2; font-weight: 600; color: var(--mq-theme-heading-1); }\n'
      + '.mq-cover-subtitle { font-size: 15pt; margin-top: 0.3em; color: #595959; }\n'
      + '.mq-cover-facts { margin-top: 2.2em; }\n'
      + '.mq-cover-facts > div { margin: 0; }\n'
      + '.mq-contents { page: contents; break-after: page; }\n'
      + '.mq-contents-title { font-size: 2.05em; font-weight: 700; color: var(--mq-theme-heading-1); margin: 0 0 0.6em; }\n'
      + '.mq-contents ol { list-style: none; margin: 0; padding: 0; }\n'
      + '.mq-contents li { margin: 0.3em 0; }\n'
      + '.mq-contents li.mq-toc-2 { padding-left: 1.4em; }\n'
      + '.mq-contents li.mq-toc-3 { padding-left: 2.8em; }\n'
      + '.mq-contents a { display: flex; align-items: baseline; color: inherit; text-decoration: none; border: 0; }\n'
      + '.mq-contents .mq-toc-fill { flex: 1; margin: 0 0.4em; border-bottom: 1px dotted #9a9a9a; }\n'
      + '.mq-note { float: footnote; font-size: 0.85em; line-height: 1.45; }\n'
      + '.mq-note .mq-note-block { display: block; margin: 0.3em 0 0; }\n'
      + '.mq-note .mq-note-code { display: block; white-space: pre-wrap; font-family: "Cascadia Mono", Consolas, monospace; font-size: 0.9em; margin: 0.3em 0 0; padding: 0.4em 0.6em; border: 1px solid var(--mq-theme-code-block-border); background: var(--mq-theme-code-block-fill); }\n'
      + 'sup.mq-note-again { font-size: 65%; vertical-align: super; line-height: normal; }\n'
      // The call in the text is a real superscript. Paged.js asks the font for superscript
      // glyphs (font-variant-position: super), and Segoe UI has none, so the first run printed
      // "one1" on the baseline at full size.
      + '[data-footnote-call]::after { vertical-align: super !important; font-size: 65% !important; line-height: 0 !important; font-variant-position: normal !important; }\n'
      // The notes area is outside .mq-preview, so the preview's link and code rules do not
      // reach a note; these are the same theme slots those rules use.
      + '.mq-note a { color: var(--mq-theme-link); text-decoration: none; }\n'
      + '.mq-note code { font-family: "Cascadia Mono", Consolas, monospace; font-size: 0.9em; padding: 0.05em 0.3em; border: 1px solid var(--mq-theme-code-inline-border); border-radius: 4px; background: var(--mq-theme-code-inline-fill); color: var(--mq-theme-code-inline-text); }\n';
  }

  /*
    The cover page, from the host's cover fields: the title, then whatever of subtitle, date,
    version and author the front matter gave. An unfilled line is left out, which is where
    this departs from Word's template (docs/Export-Alignment-Plan.md, §6.4: Word follows).
  */
  function buildCover(cover) {
    var section = document.createElement('section');
    var facts = document.createElement('div');

    section.className = 'mq-cover';

    function line(className, text, parent) {
      if (!text) { return; }

      var div = document.createElement('div');

      div.className = className || '';
      div.textContent = text;
      parent.appendChild(div);
    }

    line('mq-cover-title', cover.title, section);
    line('mq-cover-subtitle', cover.subtitle, section);

    facts.className = 'mq-cover-facts';
    line('', cover.date, facts);
    line('', cover.version, facts);
    line('', cover.author, facts);
    section.appendChild(facts);

    return section;
  }

  /*
    The contents page: every heading from the first level listed to the last
    (ContentsListing.Levels, the rule Word's contents field uses), linked to the heading, with
    an empty span for the page number that labelPages fills once the heading has landed. Its
    title is not a heading, so it neither lists itself nor appears in the PDF's bookmarks.
  */
  function buildContents(contents, root) {
    var nav = document.createElement('nav');
    var title = document.createElement('div');
    var list = document.createElement('ol');

    nav.className = 'mq-contents';
    title.className = 'mq-contents-title';
    title.textContent = contents.title;
    nav.appendChild(title);
    nav.appendChild(list);

    Array.prototype.forEach.call(root.querySelectorAll('h1, h2, h3, h4, h5, h6'), function (heading) {
      var level = Number(heading.tagName.charAt(1));

      if (level < contents.first || level > contents.last || !heading.id) { return; }

      var item = document.createElement('li');
      var link = document.createElement('a');
      var text = document.createElement('span');
      var fill = document.createElement('span');

      item.className = 'mq-toc-' + (level - contents.first + 1);
      link.setAttribute('href', '#' + heading.id);
      text.textContent = heading.textContent.replace(/\s+/g, ' ').trim();
      fill.className = 'mq-toc-fill';
      link.appendChild(text);
      link.appendChild(fill);

      var number = document.createElement('span');

      number.className = 'mq-toc-page';
      link.appendChild(number);
      item.appendChild(link);
      list.appendChild(item);
    });

    return nav;
  }

  /*
    Footnotes, moved from the end of the document to the place each is called.

    Markdig collects the notes into <div class="footnotes"><ol><li id="fn:1">, and each call is
    <a class="footnote-ref" href="#fn:1">. Paged.js places an element at the foot of the page
    when it floats there (float: footnote), so each note's content moves into a span standing
    where its first call was.

    Three things are done differently from what Paged.js would do on its own, each for a
    reason the design review found:
    - The number is the document's, written per note into the stylesheet, not Paged.js's
      footnote counter. That counter has open bugs across pages and resets (#59, #91), and
      Word numbers notes in the order they are first called, as Markdig does.
    - A note's paragraphs and code become display:block spans. Block content inside a float
      is the shape Paged.js's issues #68 and #320 fail on.
    - A second call to the same note is a plain superscript of the same number; the note is
      printed once.
    The back-link arrows go: on paper there is nowhere to go back to.

    Returns the per-note rules, and how many notes were moved, for the 'rendered' answer.
  */
  function notesToFloats(root) {
    var rules = [];
    var moved = 0;
    var seen = {};
    // Markdig's own group, by its exact shape, never the first thing wearing the class: the
    // fixture's git graph has a commit named "footnotes", mermaid makes a commit's id a class
    // on its SVG group, and that commit comes first in the document - so the first run of this
    // found the diagram, no notes in it, and moved nothing.
    var group = Array.prototype.find.call(root.querySelectorAll('div.footnotes'), function (candidate) {
      return candidate.querySelector(':scope > ol > li[id]') !== null;
    }) || null;

    Array.prototype.forEach.call(root.querySelectorAll('a.footnote-ref[href^="#fn"]'), function (call) {
      var id = decodeURIComponent(call.getAttribute('href').slice(1));
      var number = call.textContent.trim();
      var source = group ? group.querySelector(':scope > ol > li[id="' + id.replace(/"/g, '\\"') + '"]') : null;
      var anchor = call.parentElement && call.parentElement.tagName === 'SUP' && call.parentElement.childNodes.length === 1
        ? call.parentElement
        : call;

      if (!source) { return; }

      if (seen[id]) {
        var again = document.createElement('sup');

        again.className = 'mq-note-again';
        again.textContent = number;
        anchor.replaceWith(again);
        return;
      }

      seen[id] = true;

      var note = document.createElement('span');
      var key = 'mq-note-' + (moved + 1);

      note.className = 'mq-note ' + key;

      Array.prototype.forEach.call(source.querySelectorAll('.footnote-back-ref'), function (back) {
        back.remove();
      });

      Array.prototype.forEach.call(source.children, function (block, index) {
        var span = document.createElement('span');

        if (block.tagName === 'PRE') {
          span.className = 'mq-note-code';
          span.textContent = block.textContent.replace(/\n$/, '');
        } else {
          span.className = index === 0 ? '' : 'mq-note-block';
          span.innerHTML = block.innerHTML;
        }

        note.appendChild(span);
      });

      anchor.replaceWith(note);
      moved++;

      rules.push('.' + key + '[data-footnote-call]::after { content: ' + cssString(number) + '; }');
      rules.push('.' + key + '[data-footnote-marker]::marker { content: ' + cssString(number + '. ') + '; }');
    });

    if (group && moved > 0) {
      group.remove();
    }

    // What was there to work with, for the log: the first spike run moved nothing, with calls
    // and notes both in the PDF, so the next answer has to say which half it could not find.
    var firstCall = root.querySelector('a[href^="#fn"], sup.footnote-ref, .footnote-ref');

    return {
      rules: rules.join('\n'),
      moved: moved,
      found: 'calls ' + root.querySelectorAll('a.footnote-ref').length
        + ', group ' + (group ? 'yes' : 'no')
        + ', notes ' + (group ? group.querySelectorAll('li[id]').length : 0)
        + ', first call ' + (firstCall ? firstCall.outerHTML.slice(0, 160) : 'none')
    };
  }

  /*
    Whether the page can draw each face, by measuring rather than asking.

    document.fonts.check() answers true for any name it has no web font to load for, installed
    or not, so it said yes to Aptos, which WebView2 cannot see. A face is here if text set in
    it, with a generic family behind it, measures differently from the generic family alone.
  */
  function faces() {
    var found = {};
    var context = document.createElement('canvas').getContext('2d');
    var sample = 'mmmmmmmmmmlli1WQ@#';

    FACES.forEach(function (face) {
      found[face] = ['monospace', 'serif', 'sans-serif'].some(function (generic) {
        context.font = '72px ' + generic;
        var alone = context.measureText(sample).width;

        context.font = '72px "' + face + '", ' + generic;
        return context.measureText(sample).width !== alone;
      });
    });

    return found;
  }

  /*
    A heading is never the last thing on a page.

    The print stylesheet asks for break-after: avoid on headings, and Paged.js does not honor
    it: the fixture's "Setext H1" sat alone at the foot of a page, and the flowchart's heading
    one page above its diagram. So when the content that does not fit begins a block, and the
    block before it is a heading, the heading goes over with it. Not when the heading is the
    first thing on the page - then there is nowhere better for it, and moving it would loop.
  */
  function keepHeadingsWithNext(Handler) {
    return class KeepHeadingsWithNext extends Handler {
      onOverflow(overflow) {
        if (!overflow) { return undefined; }

        var start = overflow.startContainer;
        var node = start.nodeType === 1 ? (start.childNodes[overflow.startOffset] || start) : start;
        var element = node.nodeType === 1 ? node : node.parentElement;
        var block = element;

        while (block && block.parentElement && !block.parentElement.classList.contains('mq-preview')) {
          block = block.parentElement;
        }

        if (!block || !block.parentElement) { return undefined; }

        // Only when the overflow is the whole block: a paragraph split mid-way leaves its first
        // lines under the heading, which is not an orphan.
        var before = document.createRange();

        before.setStart(block, 0);
        before.setEnd(overflow.startContainer, overflow.startOffset);

        if (before.toString().trim() !== '') { return undefined; }

        var heading = block.previousElementSibling;

        if (!heading || !/^H[1-6]$/.test(heading.tagName) || !heading.previousElementSibling) {
          return undefined;
        }

        var moved = overflow.cloneRange();

        moved.setStartBefore(heading);
        return moved;
      }
    };
  }

  /*
    A table that runs onto another page carries its header row there, at the widths it had.

    Word and the old print both repeat a table's header; Paged.js does neither (its issue 36),
    and it lays each page's piece of a table out afresh, so the columns moved from one page to
    the next. When the piece of a table that opens a new page is rendered - it arrives as a
    rebuilt ancestor marked data-split-from - the source's thead is copied in, and a colgroup
    fixes each column at the width the table's first piece was given. Done as the piece is
    rendered, before the rows below it are measured, so the header's height is counted.
  */
  function repeatTableHeaders(Handler) {
    return class RepeatTableHeaders extends Handler {
      renderNode(clone, node) {
        if (!clone || clone.nodeType !== 1 || !node || node.nodeType !== 1) { return undefined; }

        var table = clone.closest('table[data-split-from]');
        var source = node.closest('table');

        if (!table || !source || table.querySelector(':scope > thead')) { return undefined; }

        var head = source.querySelector(':scope > thead');

        if (!head) { return undefined; }

        var copy = head.cloneNode(true);

        // The copy is not a piece of the source, and Paged.js finds pieces by data-ref.
        copy.removeAttribute('data-ref');
        Array.prototype.forEach.call(copy.querySelectorAll('[data-ref]'), function (e) {
          e.removeAttribute('data-ref');
        });

        var first = document.querySelector('.pagedjs_pages table[data-ref="' + table.getAttribute('data-split-from') + '"]');
        var cells = first ? first.querySelectorAll(':scope > thead > tr:first-child > *') : [];

        if (cells.length) {
          var columns = document.createElement('colgroup');

          Array.prototype.forEach.call(cells, function (cell) {
            var column = document.createElement('col');

            column.style.width = cell.getBoundingClientRect().width + 'px';
            columns.appendChild(column);
          });

          table.style.tableLayout = 'fixed';
          table.insertBefore(columns, table.firstChild);
          columns.after(copy);
        } else {
          table.insertBefore(copy, table.firstChild);
        }

        return undefined;
      }
    };
  }

  /// Lower-case roman numerals, for the contents pages: i, ii, iii, iv ...
  function roman(value) {
    var numerals = [[1000, 'm'], [900, 'cm'], [500, 'd'], [400, 'cd'], [100, 'c'], [90, 'xc'],
      [50, 'l'], [40, 'xl'], [10, 'x'], [9, 'ix'], [5, 'v'], [4, 'iv'], [1, 'i']];
    var text = '';

    numerals.forEach(function (pair) {
      while (value >= pair[0]) {
        text += pair[1];
        value -= pair[0];
      }
    });

    return text;
  }

  /*
    Every page's number, written onto the page once layout is done - the one source for the
    footer and for every contents entry.

    The cover has none. The contents pages count i, ii, iii from their first; the body counts
    1, 2, 3 from its first, as Word's three sections do (WordExport.md §4). Paged.js's own page
    counter could not do this: a counter-reset held for one page, so the body's page 1 was
    followed by page 5 while the contents, through target-counter, assumed it had held.
    Writing the labels after layout costs nothing in accuracy, because a page's number does
    not change what fits on it - only the contents entries' numbers do, and their width is
    taken up by the dotted filler.
  */
  function labelPages() {
    var labels = {};
    var contentsPage = 0;
    var bodyPage = 0;

    Array.prototype.forEach.call(document.querySelectorAll('.pagedjs_pages > .pagedjs_page'), function (page) {
      var label;

      if (page.classList.contains('pagedjs_cover_page')) {
        label = '';
      } else if (page.classList.contains('pagedjs_contents_page')) {
        label = roman(++contentsPage);
      } else {
        label = String(++bodyPage);
      }

      page.style.setProperty('--mq-page-label', cssString(label));
      page.setAttribute('data-mq-label', label);
    });

    Array.prototype.forEach.call(document.querySelectorAll('.pagedjs_pages .mq-contents a[href^="#"]'), function (link) {
      var id = link.getAttribute('href').slice(1);
      var target = document.querySelector('.pagedjs_pages [id="' + id.replace(/"/g, '\\"') + '"]');
      var page = target ? target.closest('.pagedjs_page') : null;
      var number = link.querySelector('.mq-toc-page');

      if (number && page) {
        number.textContent = page.getAttribute('data-mq-label') || '';
      }

      labels[id] = true;
    });

    return Object.keys(labels).length;
  }

  /// Every diagram that directly follows a heading, at any depth. See pageSheet for why.
  function markDiagramsAfterHeadings(root) {
    Array.prototype.forEach.call(root.querySelectorAll('pre.mermaid'), function (diagram) {
      var before = diagram.previousElementSibling;

      if (before && /^H[1-6]$/.test(before.tagName)) {
        diagram.classList.add('mq-after-heading');
      }
    });
  }

  function sheet(text) {
    var entry = {};

    entry[window.location.href] = text;
    return entry;
  }

  async function paginate(p) {
    try {
      document.title = p.title || 'Marqora';

      if (p.rootStyle) {
        document.documentElement.setAttribute('style', p.rootStyle);
      }

      // The preview's class on the wrapper, so every rule in app.css that the preview uses
      // applies here. Paged.js clones the wrapper onto each page it splits it across.
      var template = document.createElement('template');
      template.innerHTML = '<div class="mq-preview">' + (p.html || '') + '</div>';
      markDiagramsAfterHeadings(template.content);

      var root = template.content.firstElementChild;
      var furniture = p.furniture || {};
      var notes = notesToFloats(root);

      // Contents first, then the cover in front of it, so the contents lists only the
      // document's own headings. Page numbers are written after layout (labelPages).
      if (furniture.contents) {
        root.insertBefore(buildContents(furniture.contents, root), root.firstChild);
      }

      if (furniture.cover) {
        root.insertBefore(buildCover(furniture.cover), root.firstChild);
      }

      var sheets = SHEETS.slice();

      if (p.themeCss) { sheets.push(sheet(p.themeCss)); }
      sheets.push(sheet(pageSheet(p.page, p.title, furniture)));

      if (notes.rules) { sheets.push(sheet(notes.rules)); }

      window.Paged.registerHandlers(
        keepHeadingsWithNext(window.Paged.Handler),
        repeatTableHeaders(window.Paged.Handler));

      var previewer = new window.Paged.Previewer();
      var started = performance.now();
      var flow = await previewer.preview(template.content, sheets, document.body);
      var linked = labelPages();

      // How many notes reached a page foot, against how many were moved there - plan O4 needs
      // to know when Paged.js could not place one. Counted by note, not by fragment: a note
      // that runs over onto the next foot is still one note.
      var placed = {};

      Array.prototype.forEach.call(document.querySelectorAll('.pagedjs_footnote_area .mq-note'), function (note) {
        var key = Array.prototype.find.call(note.classList, function (c) { return /^mq-note-\d+$/.test(c); });

        if (key) { placed[key] = true; }
      });

      post('rendered', {
        pages: flow.total,
        layoutMs: Math.round(performance.now() - started),
        visibility: document.visibilityState,
        faces: faces(),
        notesMoved: notes.moved,
        notesFound: notes.found,
        notesPlaced: Object.keys(placed).length,
        contentsEntries: furniture.contents ? linked : 0
      });
    } catch (error) {
      post('failed', { message: String(error && error.stack || error) });
    }
  }

  window.chrome.webview.addEventListener('message', function (event) {
    var p = event.data;

    if (p && p.type === 'paginate') {
      paginate(p);
    }
  });

  post('ready');
})();
