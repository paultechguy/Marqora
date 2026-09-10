# Marqora vNext - What's New

## Replace All

`Edit > Replace All...` (`Ctrl+Shift+H`) is Find All with a **Replace with** box folded out —
the chevron beside the search box opens and closes it, so switching between finding and
replacing needs no dialog of its own. `Ctrl+H` still belongs to the editor's own replace, for
one document at a time; this one runs across every open tab at once, and runs the search itself
rather than waiting on a Find All you already ran. It asks before touching anything, naming how
many matches it found and in how many documents. In regular-expression mode the replacement can
reach into the match with `$1`, `$2`, `${name}` or `$&` — the icon beside the box lists whichever
of those the current pattern actually captured; in plain text mode the replacement goes in
exactly as typed. An empty **Replace with** box deletes every match outright, and the
confirmation says so plainly rather than leaving it to be discovered afterward.

Nothing is written to disk on its own: every document it touches simply becomes unsaved, the
same as if you had typed the change yourself, and `Ctrl+Z` takes it back one document at a time,
whether or not that tab is the one on screen. A search matching more than 5,000 times is refused
rather than run partway and left inconsistent.
