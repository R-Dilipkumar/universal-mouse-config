# Mouse Config

Set up any mouse on Windows: test its buttons, change pointer and scroll settings, and (for supported mice) change the buttons, DPI and lighting stored inside the mouse. Set it up once and close the app: nothing runs in the background, and your settings keep working.

[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Platform: Windows](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)

> **Work in progress, not released yet.** Everything below "Works with any mouse" and "Inside the mouse" is built. See [Planned](#planned) for what's next.

## Works with any mouse
- **Finds every mouse** on its own (USB, dongle, Bluetooth), live as you plug in or remove one. Touchpads are hidden.
- **Overview:** what the mouse is, who makes it (33 makers and chips known by their USB ID), how that kind of mouse is normally set up, which tools can do it, and what this app can do for it. One click opens each feature.
- **Test buttons:** press a button or turn the wheel and that part of the mouse picture lights up; a checklist ticks off each one and a list shows what the mouse sent (button, key, media key, or maker data) and on which channel. Also **measures the report rate** (move the mouse fast in circles).
- **Pointer:** speed, enhance pointer precision, swap left/right, double-click speed, ClickLock, pointer trails, hide while typing, show location with Ctrl, snap to default button, with dots to click and a folder to double-click to try them.
- **Scroll:** lines per notch (or one screen at a time) and sideways scroll characters, with a test area.
- Windows settings apply right away, with **Undo**, **Windows defaults**, and **Restore my original settings** (your settings from the first run, saved once in `%AppData%\MouseConfig\original-windows-settings.json` and never overwritten).
- **Dark, light, or match Windows** look (bottom left).

## Inside the mouse (Mouse memory tab)
For Redgear / Sunplus `1BCF:08A0` mice. More makers can be added as plugins; see [the research notes](docs/research.md).
- Reads the mouse's own settings when you open the tab (reading changes nothing) and saves a **backup** the first time, in `%AppData%\MouseConfig\backups` (never overwritten).
- **Buttons:** give any of the 7 button slots (left click too) any action: clicks, back/forward, media keys, volume, DPI up/down/cycle, lighting, show desktop, or **any keyboard key** with Ctrl, Shift, Alt or Win (click **Record…**, then press the key or click the button you want). **Press a button on the mouse to find its row.** One button always stays left click.
- **DPI:** choose which DPI step the mouse uses now (its DPI button moves through these steps).
- **Lighting:** DPI colour, multicolour, rainbow, flow, waltz, seasons or off, and the effect speed (1–8).
- Changes collect until you press **Save to mouse…** and say Yes; then the app writes only the button and DPI/lighting areas and reads the mouse back to check. **Discard** throws changes away; **Restore backup…** puts the original back.
- The mouse keeps these settings on any PC, without this app.

## Planned
- More hardware plugins, starting with Logitech (HID++), only from documented protocols and tested on a real mouse. Lighting for many brands through OpenRGB is an option.
- Export/import of settings.
- Release: a single .exe download.

## Good to know
- Nothing runs in the background: closing the window quits the app. Settings saved in the mouse and Windows' own settings keep working after that.
- Logs are saved in `%AppData%\MouseConfig\logs`, settings in `%AppData%\MouseConfig\settings.json`.
- Detection only reads what the mouse describes about itself. The button test and Mouse memory tab only listen to the chosen mouse, and only while their tab is open and the app is in front.
- Windows reports at most 5 mouse buttons. Extra buttons only show up if the mouse sends them as keys or media keys. Some buttons, like a DPI button, work inside the mouse and never reach Windows.
- Pointer and scroll settings are Windows' own, so they apply to **all** mice (and the touchpad).
- Report rate is measured from the movement reports Windows receives; very high rates may read a little low.
- Settings stored inside a mouse need a plugin for its maker or chip; there is no universal standard. The only plugin so far is for Sunplus `1BCF:08A0` (sold as Redgear and others). The app checks the mouse's identity first and never sends guessed commands. Identity `AA0CFD` is the one tested by the protocol's source; `AA0EFD` (seen on a Redgear) has the same layout as far as every check shows, and changes to it need your OK.
- The reference tool only changed button slots 4 and 5, the active DPI step and lighting. Changing slots 1–3, 6 and 7 uses the same table and checksum but hasn't been tried on a real mouse yet. Changing the DPI step values themselves isn't supported (that part of the memory isn't documented).
- Keys stored in the mouse are by position (USB key codes), so on a non-US keyboard layout a symbol key may type a different character. Shift, Ctrl, Alt or Win on their own can't be stored.
- A scroll wheel that jumps back a notch is a worn or dirty wheel sensor; the mouse can't be told to ignore it. Cleaning the sensor with contact cleaner often fixes it.

## How different mice are set up
Short version: plain mice store nothing and are fully covered by Windows' settings; gaming mice store settings with each maker's own commands; many budget brands share a few chips; newer mice are often set up in the browser. Details and sources: [docs/research.md](docs/research.md).

## Build from source
Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows.

```bash
dotnet build MouseConfig.slnx
dotnet test --solution MouseConfig.slnx
dotnet run --project src/MouseConfig.App
```

## Credits
Hardware protocol notes for the Redgear / Sunplus mouse come from [redgear-a17-linux](https://github.com/sh7vansh/redgear-a17-linux). No code was copied from it.

## License
MIT. See [LICENSE](LICENSE).
