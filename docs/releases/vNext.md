# Marqora vNext - What's New

## Fixes

**Side-by-side view keeps the line you are editing in view.** The preview used to line up only
the top of each pane, so in a file whose lines wrap in the source pane, a line near the bottom
of the source could sit well above its match in the preview, or off the top of it, and near the
end of a document the preview ran ahead of the source. The preview now keeps the line with the
cursor at the same height as it is in the source, and follows a line a third of the way down
when you scroll the cursor away. Find Next from the find box moves the preview too.
