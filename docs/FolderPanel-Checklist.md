# Folder panel checklist

The hand checks for the folder panel, in a Release build. The tests cover the listing, the tree,
the `.gitignore` rules, file-name rules, the Recycle Bin policy, launch arguments, image links and
the workspace's rename and delete. This list is only what a test cannot see: the screen, the
keyboard, the mouse, Windows, and the parts joined together.

- **Core sitting:** about an hour, once, in order, with the one test folder below. After a fix,
  repeat only the steps it touched.
- **Optional hardware:** another day, or skipped. Each needs something not every desk has.

Fill in the record at the bottom.

---

## The test folder

Make it once, before the sitting. Nothing else is needed.

```powershell
$t = "$env:USERPROFILE\Documents\FolderPanelTest"
New-Item -ItemType Directory -Force "$t\guide\images", "$t\notes", "$t\build" | Out-Null
Set-Content "$t\README.md" "# Test folder`n`nSee [the guide](guide/intro.md)."
Set-Content "$t\guide\intro.md" "# Intro`n`nSome text.`n`n## Pictures`n"
Set-Content "$t\guide\setup.md" "# Setup"
Set-Content "$t\notes\todo.md" "# To do"
Set-Content "$t\notes\locked.md" "# Locked by Windows"
Set-ItemProperty "$t\notes\locked.md" -Name IsReadOnly -Value $true
Set-Content "$t\build\output.md" "# Ignored"
Set-Content "$t\.gitignore" "build/"
Set-Content "$t\notes.txt" "not markdown"
Copy-Item "$env:WINDIR\Web\Screen\img100.jpg" "$t\guide\images\shot.jpg"
Copy-Item "$env:WINDIR\Web\Screen\img100.jpg" "$t\outside.jpg"
```

If `img100.jpg` is not there, copy any picture to those two places.

---

## Core sitting

Start with Marqora closed.

### Placement

1. **First look.** Rename `%LOCALAPPDATA%\PaulTechGuy\Marqora\settings.json` to `settings.old.json`, start
   Marqora and open `README.md`. Both panels are docked in the left-hand column, the folder panel
   offering **Open Folder** and **Use This Document's Folder**. Put the settings file back
   afterwards.
2. **Choose the root.** `Ctrl+Shift+O`, pick `FolderPanelTest`. The tree shows `build`, `guide`,
   `notes`, `README.md`, folders first; `build` is grayed with a tooltip saying `.gitignore`
   leaves it out; `README.md` is picked out as the active document.
3. **Unpin.** The folder panel's pin moves it to a tab on the strip at the window's far left,
   word reading upward, icon below. Click the tab: it slides out over the text, past the docked
   outline, which stays visible.
4. **Slide away.** Click into the text: it slides away. Slide it out, press `Escape`: it slides
   away and the keyboard is in the editor.
5. **Keys and menu.** `Alt+6` docks it; **View > Folder Panel** is ticked. `Alt+6` again moves it
   to the edge; the tick goes. `Alt+Shift+6` slides it out with the keyboard in it; `Alt+Shift+6`
   again returns to the text. Do the same with `Alt+4` and `Alt+Shift+4` for the outline.
6. **Both on the edge.** Unpin both. The strip shows two tabs; each slides out on its own, and
   the text has the whole width. Pin both again.
7. **Split.** Drag the splitter between the two panels; double-click it to go back to even.
8. **Remembered.** Unpin the outline, move the splitter, close Marqora and start it again. The
   folder, the outline on the edge and the split are all as you left them. Pin the outline.

### Browsing

9. **Open.** Click `guide`, then `intro.md`. It opens in a tab and its row is picked out. Switch
   back to the `README.md` tab: the tree follows.
10. **Keyboard.** `Alt+Shift+6`, then ↑ ↓ → ← through the tree; `Enter` on `setup.md` opens it.
    Typing a letter jumps to the next row starting with it.
11. **Filter.** Type `todo` in the box: `notes\todo.md` shows though `notes` was closed. Clear the
    box: the tree is as it was.
12. **Markers.** Type in `setup.md` without saving: its row shows the unsaved mark. Right-click
    its tab and tick **Read-Only**: the row shows the lock.
13. **Menu.** The `…` menu's **Show All Files** lists `notes.txt`; **Sort by Modified Date** keeps
    folders first and puts the newest file first after them; **Collapse All**, **Refresh** and
    **Recent Folders** do what they say. Turn the first two back off.
14. **Outside change.** In Explorer, make a new file `guide\new.md`: it appears within a second
    or so. Delete it in Explorer: it goes.

### Files

15. **Rename an open file.** Select `intro.md` (open in a tab), `F2`. The name before `.md` is
    selected. Type `a?b`: the reason shows under the box. Type `introduction`, `Enter`. The tab
    is renamed, and the notice at the top of the panel says so. **Undo** puts it back.
16. **Rename a folder.** Rename `guide` to `guide2` with `intro.md` open: the tab keeps working,
    and a line typed and saved lands in `guide2\intro.md`. Rename it back.
17. **Leave the tree.** Rename `todo.md` to `todo.txt`: Marqora asks first. Cancel.
18. **Delete.** Make `notes\scratch.md` in Explorer, open it, type without saving, then select
    its row and press `Delete`. The question names it, says the changes will be lost, the button
    reads *Delete and discard changes*, and `Enter` cancels. Answer it: the tab closes, the file
    is in the Recycle Bin and the notice says so.
19. **Refused.** Select `setup.md`, still marked read-only, and press `Delete`: the status line
    says why and nothing is asked. Right-click it: **Delete** is grayed with the reason. Do the
    same on the `guide` folder. Untick **Read-Only**.
20. **Windows read-only.** `Delete` on `notes\locked.md`: the title says *Delete read-only file*
    and a warning bar says so. Cancel.
21. **New document here.** Right-click `notes` > **New Document Here**, type a line, `Ctrl+S`:
    Save As starts in `notes`. Delete the file afterwards.
22. **Open All in Tabs.** Right-click `guide` > **Open All in Tabs**: each markdown file in it
    opens as a tab.
23. **Copy.** Right-click `shot.jpg` > **Copy > Relative Path** with `intro.md` active: the
    clipboard holds `images/shot.jpg`.

### Images

24. **Insert.** Click into `intro.md` under *Pictures*, right-click `shot.jpg` > **Insert Link in
    Source**: `![shot](images/shot.jpg)` is at the caret and the preview shows the picture.
    `Ctrl+Z` takes it out.
25. **Drag.** In Side by Side, drag `shot.jpg` into the middle of a line in the source: the cursor
    reads *Insert link*, and the link lands where you let go. `Ctrl+Z` takes it out. Drag it over
    the splitter and the preview: the no-drop sign; letting go there changes nothing.
26. **Refused.** Right-click `outside.jpg` (in the root, above `intro.md`'s folder): **Insert Link
    in Source** is grayed with the reason. Dragging it does not start, and the status line says
    why. In Preview view (`Alt+3`), **Insert Link in Source** is grayed for `shot.jpg` too.
27. **Open.** Double-click `shot.jpg`: it opens in the Windows picture viewer.

### Other ways in

28. **Tab menu.** Right-click a tab > **Use This Document's Folder**: that folder becomes the root.
    Set `FolderPanelTest` back from **Recent Folders**.
29. **Drop.** Drop the `notes` folder from Explorer on the window: it becomes the root. Drop it on
    the preview too.
30. **Command line.** With Marqora running, in a terminal in `FolderPanelTest\guide`, run
    `Marqora.exe .` and then `Marqora.exe setup.md`: the first sets the root to `guide`, the second
    opens `setup.md` in the running window, and no second window appears. Close Marqora and run
    `Marqora.exe .` again: it starts with `guide` as the root. Then, with Marqora started that way,
    rename `guide` from the panel: it is allowed.
31. **Close Folder.** **File > Close Folder**: the panel offers Open Folder again.

---

## Optional hardware

- **Narrator.** Turn it on, `Alt+Shift+6`, walk the tree. It reads a tree, each row's level and
  position (*2 of 4*), and *collapsed* or *expanded* on folders.
- **USB stick.** Make it the root and delete a file: the question says *permanently*, and the
  file is gone rather than in the Recycle Bin.
- **Mapped network drive.** Make it the root: the tree lists it. If changes made elsewhere do not
  appear, **Refresh** brings them in; the panel does not spin or freeze.
- **Second display, other scale.** Move the window to a display at a different scale and drag an
  image into the source: the link still lands under the pointer.
- **A large root.** Make your user folder the root and leave it for a few minutes while a build or
  a browser runs: Marqora stays responsive and the log shows no rescan loop.

---

## Record

| Date | Build | Core sitting | Optional | Notes |
|:---|:---|:---|:---|:---|
| | | | | |
