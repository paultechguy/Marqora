# Marqora vNext - What's New

## Preferences

File > Preferences... gathers every setting in the app onto six pages; Appearance, Editor, Preview, Files, Export & Print and Advanced, and applies each change as you make it, so you can see a font or a theme before you commit to it. Cancel puts it all back. On the Advanced page you can export the lot to a file and import it on another machine; the two machines need not be on the same version of Marqora, and an import tells you plainly about anything it could not use.

## Outline

View > Outline lists the document's headings beside it, indented by level, and highlights whichever section you are reading — the caret's in the source pane, the top of the page in preview. Click or arrow through them to move the editor and the preview together, type in the filter box to narrow a long document down, and drag the edge to give the panel more room. `Alt+4` shows and hides it — and puts the keyboard in it when it opens, since asking for the outline is usually asking to use it. `Alt+Shift+4` goes to the panel and comes back, for when it is already open and you are typing; `Escape` hands the keyboard back and leaves the panel where it is, `Enter` takes you to the text, and `Ctrl+C` copies a heading. While the keyboard is in the panel the commands that edit at the caret — the Format menu and bar, Cut, Paste and Select All — grey out, because there is no caret on screen to apply them to; everything else carries on working as usual. On the Preview page of Preferences you can stop the list at a chosen heading level.

## Spell Checker

Misspelled words are underlined in the source pane as you type; F7 turns them off. Right-click or press Ctrl+. for corrections or to add a word. Code, links, URLs, math and front matter are left alone, as are acronyms and names writtenLikeThis. The words come from Windows' own dictionary, so nothing is sent anywhere; your own additions live in a plain text file you can edit, share and import from Preferences.

## Find All Can Go Straight to the First Match

Edit > Find All lists every match and waits for you to pick one. On the Editor page of
Preferences, under FINDING, "Select the first result when a search finishes" makes it pick the
first for you as each search completes and puts the keyboard on it, so the arrow keys walk the
results straight away and Enter goes to the text. It is off by default, and it is exactly the
same as clicking that first row - which means a search across all your open tabs can switch
tabs, since the first match is often not in the document you were reading.

## Keyboard Shortcuts Quick Filter

Help | Keyboard shortcuts now has a quick filter. Type in the box to narrow the list as you go — it matches both the action and the keys, so "tab" finds the tab commands and "ctrl+shift" finds everything on that chord. Empty groups drop out, the caption shows how many matched, and the copy button copies what's on screen. Escape clears the filter.

## Standardize Button UX

Buttons now come from one set of shared styles, so they are the same size and sit in the same order wherever you meet them. The action that commits — OK, Find All, Reload — wears your Windows accent color and sits to the left of the one that backs out. Prompts that throw work away start on Cancel instead, so a stray Enter no longer discards unsaved changes when reloading a file from disk.