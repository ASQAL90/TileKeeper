# TileKeeper — User Guide (no-password build)

A plain-text companion to `user-guide_nopw.html` in this folder. This guide describes the
**TileKeeper** variant: no exit password (Exit quits immediately) and no NQVM service monitor.
For the full-featured build, see `USER_GUIDE.md`.

## What this program does

TileKeeper watches a list of programs you configure and does two things automatically:

- **Keep Running** — relaunches a program if it isn't currently running.
- **Arrange Windows** — moves a program's window back to the monitor and grid cell you assigned it.

It lives in the system tray. Right-click the tray icon for **Show** (opens settings) and **Exit**
(this build has no exit password — Exit stops the program immediately). Closing the settings window
with the X button minimizes it back to the tray — it does not stop the program.

## The three tabs

| Tab | Purpose |
|---|---|
| **Keep Apps Running** | Master list of programs to watch and relaunch. |
| **Arrange Windows** | Which screen and grid cell each program's window belongs on. |
| **Display & Settings** | Display resolution hold, optional registry settings, and update check. |

`Save` at the bottom writes everything and closes to the tray. `Reset Programs & Layout` empties
the program lists, screen assignments, and layout options in the current form; it does not delete
the saved configuration until you click `Save`. It leaves cycle intervals, display hold, and
update settings unchanged. Avoid it for everyday use.

## Keep Apps Running tab

The list has three columns: **Enabled**, **Program**, and **Path / Command**. Column widths are
sized to fit the window without horizontal scrolling; the program name and path share the
available space proportionally.

- **Enabled** — uncheck a row to temporarily exclude that program from the watchdog. The row and
  its path stay saved; re-check it to resume. This does **not** stop window positioning.
- **Add...** — opens a file picker (filtered to `.lnk`, `.appref-ms`, `.rdp`, `.vnc`, `.exe`). Pick a
  shortcut or program file; you can multi-select. A `.vnc` connection file launches through
  whichever VNC viewer Windows has associated with that extension (read from the file
  association, same as double-clicking it in Explorer) — no separate setup needed.
- **Edit...** — select a row, re-pick its file to change what it points to.
- **Remove** — deletes the selected row(s).
- **Up** / **Down** — reorders the selected row (matters for unpinned programs sharing a screen).
- **Enable Keep Running** + **Cycle (seconds)** — turns on the relaunch watchdog and sets how often
  it checks (15–30s is reasonable).

You cannot add TileKeeper itself to this list — it's blocked automatically.

### Same program, different arguments

Two shortcuts to the same `.exe` but with different arguments (e.g. two VNC connections to
different hosts, or two SCADA views) are tracked as separate entries. Closing one relaunches only
that one; the other is left alone. The Arrange Windows tab shows both with a disambiguating label, e.g.
`viewer.exe (session-a)` vs `viewer.exe (session-b)`.

## Arrange Windows tab

### Programs to Position

A second, separate list near the top of this tab. Programs added here are **not** launched or
relaunched by Keep Running — use this for something you always start by hand but still want
positioned correctly once open. Same Add / Edit / Remove buttons as the Keep Apps Running tab.

The list columns are **Enabled**, **Program**, and **Match largest**:

- **Enabled** — uncheck a row to temporarily exclude that program from positioning. Its screen
  assignment is preserved, so re-checking it resumes where it left off. This does **not** affect
  the Keep Running watchdog.
- **Match largest** — click the checkbox in this column to switch that program to picking its
  largest visible window (see Match Largest Window below). Checking a row's **Enabled** box also
  selects it so Edit/Remove apply immediately.

Every program from *either* list (Keep Apps Running tab + this list) becomes available to check off in the
four screen lists below.

### Assigning screens

Four boxes, **Screen 1**–**Screen 4**, one per physical monitor. When a screen's monitor isn't
currently connected, only its **Rows**/**Cols**/**AutoFit**/grid preview gray out live (those are
meaningless without an active monitor, and re-enable the instant it's plugged back in — no
close/reopen needed). The program checklist itself always stays clickable, even for a
disconnected screen, so you can check, uncheck, or move a program off it at any time.

- Check a program to assign it to that screen. A program can only be checked on one screen at a
  time.
- Checked rows show a number badge `(1)`, `(2)`, … and fill with that cell's tile colour — the
  checklist mirrors the grid preview. Numbers follow the order you check the boxes (**click
  order**), not the list order: the first program you check takes cell 1, the next cell 2, and so
  on.
- **Rows** / **Cols** — grid shape for that screen (`0` = automatic).
- **AutoFit** — resize windows to fill their tile; unchecked just moves them without resizing.
- **Borderless** — checked (default) runs that screen's windows without a title bar or border,
  edge-to-edge across their tiles. Unchecking switches that screen to **border mode** (below).

### Border mode (Borderless unchecked)

Windows keep a title bar and a thin frame but stay flush to their tile — no overlap onto the
neighbor, no desktop gap. The switch is instant: untick and the windows re-frame on the next
scan tick.

- The Windows drop shadow is suppressed in this mode, so no dark translucent strip appears
  between adjacent windows.
- Adding a title bar takes ~30&nbsp;px out of the window's client area. Apps that stretch their
  canvas to the window (typical SCADA/overview screens) rebuild their content into the smaller
  client automatically and are unaffected. Apps that draw at a fixed canvas size will have their
  bottom content cut off in border mode — for those, leave **Borderless** checked instead (or make
  the app scale its canvas to the client size).

### Pinning to a grid cell

Click a cell in the grid preview to open a menu of checked programs for that screen, plus
**Auto-fill (clear pin)**. Picking a program locks it to that cell every time, and its badge
number and row fill update to match the pinned cell. Choosing **Auto-fill (clear pin)**
un-pins it, and it reverts to the next free cell in click order. Grid is capped at 3×3 (9 cells)
per screen; saving with more checked programs than cells is rejected.

### Match Largest Window

Some programs open a secondary window (toolbar, splash, floating panel) alongside their real main
window. If TileKeeper is moving the wrong one, click the checkbox in the **Match largest** column for that
program in the Programs to Position list — it switches that program to always pick its largest currently
visible window instead of the normal main-window guess. Off by default; only enable if you
actually see the wrong window being moved.

## Display & Settings tab

This build has **no exit password** (Exit from the tray stops the program immediately, with no
prompt) and **no NQVM service monitor** — those features were removed for this variant. This tab
holds the display-resolution hold, the optional registry setting, and the update check.

### Display resolution hold

Clamps the display to a fixed resolution on a timer, useful when a display reports a larger
resolution than a room needs. Settings: enable checkbox, **Width** / **Height**, **Capture
current** (fills in the display's current resolution for you), **Check (sec)**.

### Application update

Shows the currently installed version and two buttons:

- **Check for updates** — asks GitHub Releases for the newest public release and reports whether
  a newer version exists. This needs an internet connection and is rate-limited for anonymous use.
- **Open release page** — opens the validated GitHub release page in your browser so you can read
  the release notes and download the package yourself.

TileKeeper never downloads or replaces its own executable. If the check fails, the release page
button still works so you can check manually.

## Walkthrough: two VNC connections, kept running, tiled on Screen 2

1. Create two Windows shortcuts to your VNC viewer's `.exe`, one per remote computer. Edit each
   shortcut so its Target ends with that computer's address, e.g. `tvnviewer.exe 192.0.2.10:5900`.
2. Keep Apps Running tab → **Add...** → select both shortcuts. They appear as two rows.
3. Check **Enable Keep Running**.
4. Arrange Windows tab → check both programs under **Screen 2**. Set **Cols** = `2`, check **AutoFit**.
5. Check **Enable Custom Position**, click **Save**.

Both connections now launch automatically, relaunch individually if either drops, and snap back
into their half of Screen 2 if dragged out of place.

## Troubleshooting

| Symptom | Check |
|---|---|
| Program never launches | Keep Running is enabled; its row's path still points to an existing file. |
| Closing one of two similar entries relaunches the wrong one | Confirm the two shortcuts' arguments actually differ — identical arguments are treated as the same entry. |
| Window won't move to its assigned screen | Enable Custom Position is checked and you clicked Save; try Match Largest Window if a small companion window is being moved instead. |
| Screen 3/4's Rows/Cols/grid look grayed out | That monitor isn't detected — connect it and those controls re-enable live. The program checklist itself stays usable either way; you can still check/uncheck/reassign programs on a disconnected screen and Save persists it normally. |
| Save rejected with a grid-size error | More programs checked than the screen's grid has cells — increase Rows/Cols or uncheck one. |
| Bottom strip of a window's content is cut off in border mode | That app draws at a fixed canvas size and doesn't stretch into the smaller client a title bar leaves behind. Check **Borderless** for that screen, or make the app scale its canvas to the client size. |
| Exit asks for a password | You are running the wrong build — the no-password variant exits immediately with no prompt. |
| Configuration file was deleted or is missing | The app recreates it automatically on next launch — it restarts itself once (you may see the tray icon appear twice in quick succession) so the new file takes effect, then starts normally with default settings. |

## Glossary

- **Keep Running** — auto-relaunch a program if it isn't running.
- **Custom Position** — auto-move a window back to its assigned screen/cell.
- **AutoFit** — resize windows to fill their grid tile.
- **Grid cell** — one rectangle of a screen's tiling layout (up to 3×3).
- **Pinning** — locking a program to a specific grid cell instead of auto-fill order.
- **Position-only entry** — added via the Arrange Windows tab's own list; positioned but never launched.
- **Match Largest Window** — position by biggest visible window, for programs with extra small windows.
