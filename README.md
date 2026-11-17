# Teleprompter for Windows

A small teleprompter that sits at the top of your screen, near your webcam, and stays on top of other windows. It's inspired by NotchPrompter for macOS. Instead of voice recognition, it scrolls your script at a steady speed that you can adjust.

## Run
Double-click `Teleprompter.exe`. It needs nothing else installed. While it runs, its icon shows in the system tray.

## Rebuild after editing the code
Run `build.bat`. It uses the C# compiler that comes with Windows.

## Controls
| Action | Keys / mouse |
|---|---|
| Play / pause | `Space` or double-click |
| Faster / slower | `Up` / `Down` (Shift for steps of 5), or Shift + mouse wheel |
| Scroll manually | Mouse wheel, `Left`/`Right`, `PgUp`/`PgDn` |
| Restart from top | `R` or `Home` |
| Edit script & settings | `E` or `Enter` |
| Open a .txt file | `O` |
| Font size | Ctrl + mouse wheel, `Ctrl` + `+`/`-` |
| Move / resize | Drag the window / drag its edges |
| More options | Right-click (or the tray icon) |

**Global shortcuts** (these work while another app such as Zoom, Teams or PowerPoint is active):
- `Ctrl+Alt+Space`: play / pause
- `Ctrl+Alt+=` / `Ctrl+Alt+-`: faster / slower
- `Ctrl+Alt+R`: restart
- `Ctrl+Alt+H`: show / hide

## Notes
- Speed ranges from 1 to 100. The text moves at the speed value × 2 pixels per second.
- The window is **hidden from screen sharing and recording** by default, so viewers can't see it. You can turn this off from the right-click menu or in Settings. This needs Windows 10 version 2004 or later.
- Your script and settings are saved automatically in `%APPDATA%\Teleprompter`.
