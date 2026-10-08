/*
  The paged print page's side of the conversation with its host.

  The host posts one 'paginate' message carrying the document's print markup, the theme
  stylesheet the preview had in force, the preview root's own properties (the accent arrives
  that way), the page setup and a title. This lays the document out into pages with Paged.js
  and answers 'rendered' with what it measured, or 'failed' with why.

  Every PDF and printout Marqora makes comes through here (docs/Export-Alignment-Plan.md, D2
  and §6): the document's, the cheatsheet's and a diagram pop-out's. The furniture - cover
  page, contents with page numbers, running header and page number - is the shared export
  layout Word reads too, which the host sends; footnotes go to the foot of the page they are
  called on, numbered as the document numbers them.
*/

(function () {
  'use strict';

  /// The sheets the preview uses, in the order shell.html links them. Monaco's is not one:
  /// there is no editor here.
  var SHEETS = ['vendor/katex/katex.min.css', 'app.css', 'syntax.css'];

  /*
    What each kind of page adds to a document's: its own sheet, and the class its markup is
    wrapped in beside .mq-preview so that sheet's rules find it. The cheatsheet's sheet keys
    its paper rules on .mq-cheatsheet-doc and its window's typography on .mq-cheatsheet, so on
    paper the samples keep their shape and the type is the paper spec's. A diagram pop-out
    needs nothing: its one drawing comes as a pre.mermaid, which the print block already holds
    to one page.
  */
  var KINDS = {
    cheatsheet: { sheets: ['cheatsheet.css'], wrapper: 'mq-cheatsheet-doc' }
  };

  /// The paper spec's four face roles, whose family chains arrive as custom properties.
  var FACE_ROLES = ['text', 'display', 'code', 'diagram'];

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

    // The header and footer bands, set as the paper spec sets them (PaperSpec.RunningHeader
    // and RunningFooter), as Word sets its own.
    var band = function (element) {
      return 'font-family: var(--mq-paper-' + element + '-family); font-size: var(--mq-paper-' + element + '-size); color: #767676;';
    };

    // The running header and page number, when the export asks for them - the same switch
    // as Word's header and footer. The number is each page's own label, written onto the page
    // after layout (labelPages), never Paged.js's page counter: that counter honored a reset
    // for one page only, so the body's page 1 was followed by page 5.
    var boxes = furniture && furniture.headerAndFooter
      ? '  @top-left { content: ' + cssString(title) + '; ' + band('header') + ' }\n'
        + '  @bottom-right { content: var(--mq-page-label, ""); ' + band('footer') + ' }\n'
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

    // One paper element's type, as the paper spec states it (PaperSpec in Domain): its face,
    // size, weight, slant, case and line height. Nothing in this page names a font or a size
    // of its own.
    function set(element) {
      var p = '--mq-paper-' + element + '-';

      return 'font-family: var(' + p + 'family); font-size: var(' + p + 'size); font-weight: var(' + p + 'weight); '
        + 'font-style: var(' + p + 'style); text-transform: var(' + p + 'transform); line-height: var(' + p + 'line);';
    }

    return '.mq-cover { page: cover; break-after: page; padding-top: ' + third + '; }\n'
      + '.mq-cover-title { ' + set('cover-title') + ' color: var(--mq-theme-heading-1); }\n'
      + '.mq-cover-subtitle { ' + set('cover-subtitle') + ' margin-top: 0.3em; color: #595959; }\n'
      + '.mq-cover-facts { margin-top: 2.2em; }\n'
      + '.mq-cover-facts > div { margin: 0; }\n'
      + '.mq-contents { page: contents; break-after: page; }\n'
      + '.mq-contents-title { ' + set('h1') + ' color: var(--mq-theme-heading-1); margin: 0 0 0.6em; }\n'
      + '.mq-contents ol { list-style: none; margin: 0; padding: 0; }\n'
      + '.mq-contents li { ' + set('contents-entry') + ' margin: 0.3em 0; }\n'
      + '.mq-contents li.mq-toc-2 { padding-left: 1.4em; }\n'
      + '.mq-contents li.mq-toc-3 { padding-left: 2.8em; }\n'
      + '.mq-contents a { display: flex; align-items: baseline; color: inherit; text-decoration: none; border: 0; }\n'
      + '.mq-contents .mq-toc-fill { flex: 1; margin: 0 0.4em; border-bottom: 1px dotted #9a9a9a; }\n'
      + '.mq-note { float: footnote; ' + set('footnote') + ' }\n'
      + '.mq-note .mq-note-block { display: block; margin: 0.3em 0 0; }\n'
      + '.mq-note .mq-note-code { display: block; white-space: pre-wrap; font-family: var(--mq-face-code); font-size: 0.9em; margin: 0.3em 0 0; padding: 0.4em 0.6em; border: 1px solid var(--mq-theme-code-block-border); background: var(--mq-theme-code-block-fill); }\n'
      + 'sup.mq-note-again { font-size: 65%; vertical-align: super; line-height: normal; }\n'
      // The call in the text is a real superscript. Paged.js asks the font for superscript
      // glyphs (font-variant-position: super), and Segoe UI has none, so the first run printed
      // "one1" on the baseline at full size.
      + '[data-footnote-call]::after { vertical-align: super !important; font-size: 65% !important; line-height: 0 !important; font-variant-position: normal !important; }\n'
      // The notes area is outside .mq-preview, so the preview's link and code rules do not
      // reach a note; these are the same theme slots those rules use.
      + '.mq-note a { color: var(--mq-theme-link); text-decoration: none; }\n'
      + '.mq-note code { font-family: var(--mq-face-code); font-size: 0.9em; padding: 0.05em 0.3em; border: 1px solid var(--mq-theme-code-inline-border); border-radius: 4px; background: var(--mq-theme-code-inline-fill); color: var(--mq-theme-code-inline-text); }\n';
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
  /*
    The notes left where Markdig put them: a numbered list at the end of the document. The
    host asks for this when a first layout could not place every note at the foot of its page
    (plan O4): the whole document then takes endnotes, one model and never a mix, and the
    report says why. The back-link arrows still go - paper has nowhere to go back to.
  */
  function notesAsEndnotes(root) {
    var group = Array.prototype.find.call(root.querySelectorAll('div.footnotes'), function (candidate) {
      return candidate.querySelector(':scope > ol > li[id]') !== null;
    }) || null;

    if (!group) { return { rules: '', moved: 0, found: 'endnotes, no group' }; }

    Array.prototype.forEach.call(group.querySelectorAll('.footnote-back-ref'), function (back) {
      back.remove();
    });

    return { rules: '', moved: group.querySelectorAll(':scope > ol > li[id]').length, found: 'endnotes' };
  }

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

    The faces checked are the ones the paper spec names, read from its own properties, so this
    page names none itself (PaperFaces is the one place).
  */
  function faces() {
    var found = {};
    var context = document.createElement('canvas').getContext('2d');
    var sample = 'mmmmmmmmmmlli1WQ@#';
    var root = getComputedStyle(document.documentElement);
    var names = [];

    FACE_ROLES.forEach(function (role) {
      root.getPropertyValue('--mq-face-' + role).split(',').forEach(function (family) {
        var name = family.trim().replace(/^["']|["']$/g, '');

        if (name && !/^(serif|sans-serif|monospace|system-ui)$/.test(name) && names.indexOf(name) < 0) {
          names.push(name);
        }
      });
    });

    names.forEach(function (face) {
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
  /*
    A print of some of the pages: every page outside the range is taken out of the print, so
    the printer is handed the whole of what is left and never a page range.

    Chromium was handed the range once, and any range that did not start at page 1 hung the
    job - page 5 alone spooled for ever on an HP LaserJet and on Microsoft Print to PDF alike.
    Doing it here costs nothing: the pages are already laid out and already labeled, so page 5
    still says "iv" and its contents entries still point where they did.

    The range reads as the print dialog writes it: page numbers counted from the first sheet,
    cover included, as "5", "5-6", "1-3, 7" or "9-" for the ninth onward. Answers how many
    pages are kept: all of them when there is no range, none when it names no page.
  */
  function keepPages(spec, total) {
    if (!spec || !String(spec).trim()) { return total; }

    var wanted = {};

    String(spec).split(',').forEach(function (part) {
      var m = /^\s*(\d+)\s*(?:-\s*(\d*)\s*)?$/.exec(part);

      if (!m) { return; }

      var from = parseInt(m[1], 10);
      var to = m[2] === undefined ? from : (m[2] === '' ? total : parseInt(m[2], 10));

      for (var n = Math.max(1, from); n <= Math.min(total, to); n++) { wanted[n] = true; }
    });

    var kept = 0;

    Array.prototype.forEach.call(document.querySelectorAll('.pagedjs_pages > .pagedjs_page'), function (page, index) {
      if (wanted[index + 1]) {
        kept++;
      } else {
        page.style.display = 'none';
      }
    });

    return kept;
  }

  /*
    The paper spec, as the laid-out pages actually draw it: one element of each kind, measured
    with getComputedStyle (docs/Export-Alignment-Plan.md, §8, the third check). The host holds
    each against PaperSpec and logs what drifted - a rule in app.css that outranks the spec's
    custom property, or a property Paged.js rewrote, shows here and nowhere else.

    The first element of a kind on a body page, outside a cover or a contents page, so the
    contents' own heading is not taken for the document's. A kind the document does not have is
    left out. Sizes are in CSS pixels, 96 to the inch; the host turns them into points.
  */
  var MEASURED = {
    'body': '.mq-preview > p',
    'h1': '.mq-preview > h1',
    'h2': '.mq-preview > h2',
    'h3': '.mq-preview > h3',
    'h4': '.mq-preview > h4',
    'h5': '.mq-preview > h5',
    'h6': '.mq-preview > h6',
    // The code, not its pre: the paper rules size the code element, and the pre keeps the
    // body's size, so measuring the pre reported 11pt against a spec the page already met.
    'code-block': '.mq-preview pre:not(.mermaid) > code',
    'code-inline': '.mq-preview p > code',
    'quote': '.mq-preview > blockquote',
    'table': '.mq-preview td',
    'footnote': '.pagedjs_footnote_area .mq-note',
    'caption': '.mq-preview figcaption',
    'callout-title': '.markdown-alert-title',
    'contents-entry': '.mq-contents li',
    'cover-title': '.mq-cover-title',
    'cover-subtitle': '.mq-cover-subtitle'
  };

  function measureStyles() {
    var measured = {};

    Object.keys(MEASURED).forEach(function (name) {
      var within = /^(contents|cover)/.test(name)
        ? '.pagedjs_pages '
        : '.pagedjs_page:not(.pagedjs_cover_page):not(.pagedjs_contents_page) ';
      var element = document.querySelector(within + MEASURED[name]);

      if (!element) { return; }

      var style = getComputedStyle(element);

      measured[name] = {
        size: parseFloat(style.fontSize),
        weight: parseInt(style.fontWeight, 10),
        italic: style.fontStyle === 'italic',
        line: parseFloat(style.lineHeight) || 0
      };
    });

    return measured;
  }

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
      var kind = KINDS[p.kind] || { sheets: [], wrapper: '' };
      template.innerHTML = '<div class="mq-preview' + (kind.wrapper ? ' ' + kind.wrapper : '') + '">' + (p.html || '') + '</div>';
      markDiagramsAfterHeadings(template.content);

      var root = template.content.firstElementChild;
      var furniture = p.furniture || {};
      var notes = p.endnotes ? notesAsEndnotes(root) : notesToFloats(root);

      // Contents first, then the cover in front of it, so the contents lists only the
      // document's own headings. Page numbers are written after layout (labelPages).
      // Not when it would list nothing: an empty contents page took the document's own H1
      // onto it, as though the title were part of the contents, and Word's empty field said
      // "No table of contents entries found." Neither export writes one now.
      var contentsNav = furniture.contents ? buildContents(furniture.contents, root) : null;

      if (contentsNav && contentsNav.querySelector('li')) {
        root.insertBefore(contentsNav, root.firstChild);
      } else {
        furniture.contents = null;
      }

      if (furniture.cover) {
        root.insertBefore(buildCover(furniture.cover), root.firstChild);
      }

      var sheets = SHEETS.concat(kind.sheets);

      if (p.themeCss) { sheets.push(sheet(p.themeCss)); }
      if (p.paperCss) { sheets.push(sheet(p.paperCss)); }
      sheets.push(sheet(pageSheet(p.page, p.title, furniture)));

      if (notes.rules) { sheets.push(sheet(notes.rules)); }

      window.Paged.registerHandlers(
        keepHeadingsWithNext(window.Paged.Handler),
        repeatTableHeaders(window.Paged.Handler));

      var previewer = new window.Paged.Previewer();
      var started = performance.now();
      var flow = await previewer.preview(template.content, sheets, document.body);
      var linked = labelPages();
      var kept = keepPages(p.pages, flow.total);

      if (kept === 0) {
        post('failed', { message: 'The page range "' + p.pages + '" names none of the ' + flow.total + ' pages.' });
        return;
      }

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
        kept: kept,
        computed: measureStyles(),
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
