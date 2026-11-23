# plan.md — Mouse_config

_Last updated: 2026-11-08 · Status: **ready to build**_

> This file is written by the **planning session** only. The **build session** reads it and builds from it.
> Build session: read this whole file first, then `session.md` for what's already built. Build in the order of "Steps to build", one step at a time, and test each on the real mouse before the next.

---

## 1. Goal
A Windows app that works with **any mouse** plugged into the PC (wired, dongle or Bluetooth):
- Finds the mouse on its own and works out what it has: how many buttons, scroll wheel, tilt wheel, extra channels.
- Shows every setting that can be changed, from Windows (software) and from the mouse itself (hardware).
- Lets the user change them in a **dark, feature-rich** screen that is still easy to understand.

## 2. Answered by the user
- Version 1: **Windows only**. No touchpad (hide the laptop's ELAN touchpad from the list).
- Look: **dark theme, lots of functions**.
- Must work for **all kinds of mice**, not just the user's.
- User's mouse: **Redgear**, model not printed (user thinks A-15). No maker software, no CD, maker website broken.

## 3. What's possible (verdict)
| Layer | What | Any mouse? |
|---|---|---|
| 1. Detect | Name, maker, button count, wheel, tilt wheel, report rate | Yes. "Find my buttons" test confirms what the mouse claims. |
| 2. Windows settings | Pointer speed, precision, scroll lines, swap buttons, double-click speed, etc. | Yes, but Windows applies them to **all** mice at once. |
| 3. Software remap | Any button → another action | Buttons 1–5 + wheel: yes (app must be running). Buttons 6+: only if the mouse sends them as keys/media keys. |
| 4. Hardware settings | DPI, lighting, on-mouse button actions | **No universal standard.** One plugin per chip/brand. v1 ships a plugin for the user's chip (see section 5). |

No app anywhere does layer 4 for every mouse (libratbag on Linux, Mouser for Logitech on Windows, both brand-by-brand).

## 4. User's mouse (checked 2026-11-08 on this laptop, read-only)
- USB ID `VID_1BCF PID_08A0` (Sunplus chip, "SPCP19x"). Same ID is sold as Redgear A-17, Philips SPK9304 and others, so the user's mouse may really be an A-17. The identity check (section 5) decides, not the name.
- HID channels found (read-only probe `tools/hidcaps.ps1`):

| Channel | Usage page / usage | Reports | Meaning |
|---|---|---|---|
| COL01 | 0x01 / 0x02 (mouse) | Input ID 1, 7 bytes: X, Y (16-bit), wheel | Normal mouse. **No tilt wheel.** |
| COL02 | 0x01 / 0x06 (keyboard) | Input 8 bytes | Buttons set to send keys use this |
| COL03 | 0x0C / 0x01 (media keys) | Input 3 bytes | Buttons set to media actions use this |
| COL04 | **0xFF00 / 0x01 (vendor)** | **Feature report ID 4, 8 bytes** | **Settings channel (DPI, lighting, buttons)** |
| COL05 | 0xFF01 / 0x01 (vendor) | Input ID 5, 6 bytes | Unknown, ignore |

COL04 matches exactly what the open-source Linux tool `redgear-a17-linux` uses, so its protocol should work here.

## 5. Redgear / Sunplus 1BCF:08A0 hardware protocol
Source: github.com/sh7vansh/redgear-a17-linux (`redgear.py`, read 2026-11-08). **That repo has no license: do not copy its code.** Write our own implementation from these notes and credit the project in our README.

**Transport:** HID feature reports on COL04. Every frame is 8 bytes: `[0x04, command, args…]`, padded with zeros. Windows: `HidD_SetFeature` / `HidD_GetFeature` with an 8-byte buffer, byte 0 = 0x04.
**Each command:** SetFeature → wait 15 ms → GetFeature, up to 8 tries, 15 ms apart. Reply byte 0 = 0x04; byte 7 = status: `0xCC` done, `0x00` not ready (retry the Get only), anything else = error. **Never re-send a command that carries data**; only retry the Get.

| Cmd | Meaning | Args / reply check |
|---|---|---|
| 0x01 | Handshake (enter config mode) | reply byte 1 must be 0xA6 |
| 0x03 | Identity | reply bytes 1–3 must equal `AA 0C FD`, else **unsupported: read-only** |
| 0x04 | Read setup | addr_lo, addr_hi, size_hi, size_lo |
| 0x05 | Read chunk | reply bytes 1–6 = 6 data bytes; repeat ceil(size/6) times |
| 0x06 | Write setup | addr_lo, addr_hi, size_hi, size_lo |
| 0x07 | Write chunk | 6 data bytes (pad with 0xFF); reply bytes 1–6 must echo them |
| 0x08 | Unknown, sent at session end before 0x02 (maybe "save") | copy this order; verify on real mouse |
| 0x02 | Exit config mode | **always send, in a `finally`** |

**Config block:** read 255 bytes from address 0. Only write inside 0–254.
- `[2]` number of DPI slots (1–7); `[6 .. 6+count]` DPI codes 1–12 → 500, 800, 1000, 1200, 1600, 2000, 2400, 3200, 4000, 4800, 6400, 8000.
- `[47]` main checksum = `(sum([0..46]) & 0xFF) ^ 0x5A`. The reference tool **keeps it as found** (author's mouse had a stale one). Do the same; show "checksum stale" as info only.
- `[48]` low nibble = active DPI slot − 1 (keep high nibble); `[49]` = `[48] ^ 0xFF`.
- `[50]` low nibble = lighting mode (1 dpi, 2 multicolour, 3 rainbow, 4 flow, 5 waltz, 6 seasons, 7 off); bits 5–7 = `8 − speed` (speed 1–8); bit 4 must be 0. `[51]` = `[50] ^ 0xFF`.
- `[52] ^ [53]` must be 0xFF (also checked at 0x30/0x32/0x34 per reference).
- `[64 + 3*i .. +3]`, i = 0–6: 3-byte action per button. Button 4 at 73, button 5 at 76. Only 4 and 5 are remappable; at least one button must stay "left".
- `[118]` button checksum = `(sum([64..117]) & 0xFF) ^ 0x5A`. Recompute on every button change; refuse button edits if it's already wrong.
- Writes used: addr 48 (6 bytes) and addr 64 (60 bytes).
- Action codes: left 010100, right 010200, middle 010400, back 010800, forward 011000, disabled 000000, dpi-cycle 050000, dpi-up 070000, dpi-down 080000, led-switch 090000, led-mode 100000, key-mode 0F0000, play-pause 0300CD, stop 0300B7, prev 0300B6, next 0300B5, vol-up 0300E9, vol-down 0300EA, mute 0300E2, browser-back 030224, browser-forward 030225, desktop 020807.
- **Polling rate: not supported** by this protocol.

**Safety rules for this plugin (must have):**
1. Handshake + identity must pass before anything else; otherwise show hardware as read-only "unsupported".
2. First time a mouse is seen: save its 255-byte config to `%AppData%\MouseConfig\backups\<usb-id>-<YYYY-MM-DD-HHmm>.bin`. Never overwrite old backups. "Restore backup" button in Hardware tab.
3. Validate the block before any write. Write only changed regions. Re-read after writing; any difference = loud error.
4. Always exit config mode (0x02) even on error. One hardware session at a time (lock).
5. **First real write only with the user present and approving** (build step 8).
6. Never send guessed commands for any other mouse.

## 6. Version 1 features
**Device list (left side)**
- All connected mice, live (plug/unplug updates). Name (HID product string, else "USB mouse 1BCF:08A0"), connection, button count, report rate, wheel/tilt. Touchpads hidden.
- "Details" panel: USB ID, HID channels, what's supported per layer.

**Buttons tab**
- Mouse picture with numbered buttons (generic shape; numbers from detection). Click a button → action picker.
- "Find my buttons" test: press each button/wheel; it lights up. Shows which channel it came from (mouse / keyboard / media).
- Actions (software): default, disabled, other mouse button, key or shortcut (record it), media key, volume, open app/file/website, copy/paste/undo, switch desktop, show desktop, "type text".
- Where the hardware plugin supports it (Redgear buttons 4–5): option "save in mouse" with the hardware action list.

**Pointer tab** (Windows settings)
- Pointer speed (1–20), "enhance pointer precision" on/off, swap left/right, double-click speed + test box, ClickLock, hide pointer while typing, pointer trails, Ctrl to show pointer location, snap to default button.
- Small test area to feel the speed before keeping it.

**Scroll tab**
- Lines per notch (or one screen at a time), horizontal scroll characters, reverse scroll direction (done by the app), scroll speed boost (app).

**Hardware tab** (only with a plugin; otherwise greyed out with the reason)
- Redgear plugin: DPI slots (count 1–7, value per slot, active slot), lighting mode + speed, buttons 4–5 on-mouse actions, backup / restore, raw config dump view (advanced, read-only).

**Profiles tab**
- Profiles = button actions + scroll/pointer choices. Default profile + per-app profiles (by .exe). Switches when the front window changes.
- "Pause remapping" switch + global hotkey; per-game "disable app" list (anti-cheat safety).

**App settings**
- Start with Windows (tray), dark theme, run as admin option (so remaps work in admin windows), open settings folder, export/import profiles.

**Everywhere**
- Changes apply right away. Undo + "Reset" per tab. "Restore my original Windows settings" (snapshot taken on first run).
- Badges: "Saved in mouse" vs "Needs this app running".
- Unsupported things shown greyed with a reason, never hidden.
- Errors shown in a status line and logged to `%AppData%\MouseConfig\logs`.

**Not in v1 (later):** per-mouse pointer speed (needs a driver), macros with timing/recording, other brand plugins, Bluetooth battery level, other languages.

## 7. Tech choices (researched 2026-11-08)
| Need | Pick | Why / notes |
|---|---|---|
| Language / runtime | C#, **.NET 10 (LTS)**, `net10.0-windows` | User has shipped .NET 10 WPF before (DriveMate). |
| UI | **WPF** + built-in Fluent theme, `ThemeMode="Dark"` | Still marked experimental (warning WPF0001) in .NET 10: add `<NoWarn>WPF0001</NoWarn>`. If it misbehaves, fall back to own dark styles. |
| MVVM | CommunityToolkit.Mvvm | Pin exact latest stable at build time. |
| Tray icon | **H.NotifyIcon.Wpf 2.4.1** (has net10 target) | Check NuGet for a newer stable before pinning. |
| Find mice | Raw Input: `GetRawInputDeviceList` + `GetRawInputDeviceInfo` (`RIDI_DEVICEINFO` → buttons, sample rate, h-wheel; `RIDI_DEVICENAME` → path/USB ID) | Universal. Touchpads: skip devices whose HID has a digitizer/touchpad collection (usage page 0x0D). |
| HID details + Redgear talk | Own P/Invoke to `hid.dll` + `setupapi.dll` (`HidD_GetPreparsedData`, `HidP_GetCaps`, `HidD_GetProductString`, `HidD_Set/GetFeature`) | Proven by the probe script below; no extra library. Open COL04 with read/write share mode. |
| Button test | Raw Input `WM_INPUT` with `RIDEV_INPUTSINK` for mouse, keyboard, media (0x0C/0x01) | Tells which device and channel each press came from. Note: Windows' mouse driver only reports buttons 1–5. |
| Software remap | `WH_MOUSE_LL` hook + `SendInput` | Skip events flagged `LLMHF_INJECTED` to avoid loops. Hook callback must return fast (Windows drops slow hooks). |
| Windows settings | `SystemParametersInfo` (SPI_GET/SETMOUSESPEED, SPI_GET/SETMOUSE, WHEELSCROLLLINES/CHARS, MOUSEBUTTONSWAP, DOUBLECLICKTIME, MOUSECLICKLOCK, MOUSEVANISH, MOUSETRAILS, MOUSESONAR, SNAPTODEFBUTTON) with `SPIF_UPDATEINIFILE \| SPIF_SENDCHANGE` | Applies to all mice. |
| Per-app profiles | `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` → process name | |
| Saved settings | JSON (`System.Text.Json`) in `%AppData%\MouseConfig\settings.json` with `schemaVersion` | Lesson: bump version + migration + test when the format changes. |
| Start with Windows | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | No admin needed. |
| Tests | xunit.v3 | Lessons: `global.json` needs `"test": {"runner": "Microsoft.Testing.Platform"}`; pass `TestContext.Current.CancellationToken`. |
| Release | `dotnet publish` self-contained single .exe (win-x64), zipped, GitHub release | Unsigned: SmartScreen warns once. |

**Project layout (suggested):**
- `src/MouseConfig.App` (WPF UI, tray)
- `src/MouseConfig.Core` (device detection, settings, profiles, remap engine, plugin interface `IHardwarePlugin`)
- `src/MouseConfig.Plugins.Sunplus1BCF` (Redgear protocol; pure byte logic separate from the HID transport so it can be tested with fake bytes)
- `tests/MouseConfig.Tests`
- Plugins compiled into the app (no separate loading at runtime). Keeps it simple and avoids the DriveMate plugin-loading bug.

## 8. Steps to build (small steps, real mouse after each)
1. **Repo + skeleton:** solution, projects, `.gitignore`, `.gitattributes`, MIT LICENSE (Dilipkumar R, 2026), README stub. Empty dark main window (fits `SystemParameters.WorkArea`, centered) + tray icon. Run it.
2. **Detect mice:** list + details panel, live plug/unplug. Check: user's Redgear shows, ELAN touchpad hidden.
3. **Find my buttons:** live test with channel shown. Check on the real mouse: every button, wheel, DPI button (expect it may not show).
4. **Pointer + Scroll tabs:** read/write Windows settings, snapshot originals, Undo/Reset, test areas.
5. **Software remap:** hook + action picker + saving. Pause switch + hotkey.
6. **Profiles:** per-app switching, disable list.
7. **Redgear plugin, read-only:** handshake, identity, read 255 bytes, save backup, show DPI/lighting/buttons in the Hardware tab. Unit tests on parse/validate/checksum with fake bytes. **User present.** Exit config mode always.
8. **Redgear plugin, writes:** DPI, lighting, buttons 4–5. **Ask the user before the first write.** Read back after writing; unplug + replug to check settings survived (tells us what 0x08 does). Test restore from backup.
9. **App settings:** start with Windows, admin option, export/import.
10. **Finish:** README (features, download, limits, credit to redgear-a17-linux for the protocol notes), screenshots, GitHub repo + topics, release .exe, portfolio update (`Resume_Portfolio`), session.md.

**Repo name ideas:** `universal-mouse-config` (suggested), `any-mouse-config`, `mouse-tuner-windows`.

## 9. Needs the user's hands (plan for these up front)
- Mouse plugged in for tests in steps 2–3, 7–8.
- Approve the first hardware write (step 8) and do the unplug/replug check.
- Click through the SmartScreen warning on first run.
- Create the GitHub repo if Claude's permission check blocks it (lesson: give the user the command).
- Maybe approve "run as administrator" when testing remaps in admin windows.

## 10. Doubts and risks
- **Wrong model guess:** user thinks A-15; the chip ID matches the A-17 tool. Identity check decides. If identity ≠ `AA0CFD`, hardware stays read-only.
- **Command 0x08** meaning unknown; test in step 8 whether settings survive a replug.
- **DPI button** may never reach Windows (handled inside the mouse).
- **Anti-cheat games** may block or flag the hook: per-game disable list.
- **Admin windows:** remaps need the app to run as admin there.
- **Windows settings are global**, not per mouse.
- **Buttons 6+** on other mice: only remappable if they arrive as keys/media keys.
- **Unsigned app:** SmartScreen warning.

## 11. Checked before starting (preflight, 2026-11-08)
- Mouse ID, HID channels and settings channel checked on this laptop with a read-only probe.
- Redgear protocol from `redgear-a17-linux` (Python, Oct 2026, unlicensed, Fedora-tested only, Linux hidraw). Facts only, no code copied.
- WPF ThemeMode still experimental in .NET 10 (WPF0001).
- H.NotifyIcon.Wpf 2.4.1 targets net10.
- Windows mouse driver reports only buttons 1–5; extra buttons need their own HID channel.
- No Windows tool configures hardware for every mouse (libratbag Linux only; Mouser Logitech only).
- Lessons that apply: back up before overwriting (mouse config); fail loudly (hardware errors); test on the real device; pin versions; list user's-hands steps; .NET 10 test runner + xUnit1051; WPF window fits WorkArea; ToggleButton use Checked/Unchecked; frozen brushes; plugin ALC issue avoided by compiling plugins in.

**Probe script:** `tools/hidcaps.ps1` (read-only, used 2026-11-08). Opens each `vid_1bcf&pid_08a0` HID path with zero access and prints `HidP_GetCaps` and value caps. Run: `powershell -File tools\hidcaps.ps1`.

## 12. Decisions log
- 2026-11-08: This session is for planning only; building happens in a separate session that reads `plan.md`.
- 2026-11-08: Goal: configure any mouse; auto-detect buttons and settings.
- 2026-11-08: v1 Windows only, no touchpad, dark theme, lots of functions.
- 2026-11-08: Universal for detect + Windows settings + software remap; hardware via per-chip plugins. First plugin: Sunplus 1BCF:08A0 (user's Redgear), using protocol notes from redgear-a17-linux, own code. No guessed hardware commands.
- 2026-11-08: Tech: .NET 10 WPF, own HID P/Invoke, Raw Input, low-level hook, plugins compiled in.
