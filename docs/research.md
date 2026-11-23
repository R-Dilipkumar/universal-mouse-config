# How different mice are configured (research notes)

_Checked 2026-11-09. Plain-words summary of how mouse settings work across makers, and what that means for Mouse Config._

## The short answer

There is **no universal standard** for the settings stored inside a mouse (DPI, lighting, on-mouse button jobs, report rate).
Every maker uses its own commands, and often each model is different. What *is* universal on Windows:

| Works for every mouse | How Mouse Config does it |
|---|---|
| Finding the mouse, its buttons, wheel, tilt wheel | Windows Raw Input + the mouse's HID description |
| Testing every button | Raw Input, only while the Test tab is open |
| Measuring the report rate | Counting the mouse's movement reports per second |
| Pointer speed, precision, double-click, scroll lines… | Windows' own settings (they apply to all mice at once) |


Settings **inside** the mouse need a plugin per maker or chip. Mouse Config has one so far: Sunplus `1BCF:08A0` (Redgear and others).

## The kinds of mice

1. **Plain mice (most office and budget mice).** They store nothing. Everything is done by Windows or software on the PC.
   Mouse Config covers these fully.
2. **Mice with their own memory and maker software.** DPI, lighting and button jobs are stored inside the mouse using the maker's own commands:
   - Logitech: HID++ protocol (G HUB, Logi Options+). Open tools: Solaar, libratbag (Linux), OpenMouse (browser). Well documented by these projects, e.g. HID++ 2.0 features `0x2201` (adjustable DPI) and `0x8060` (report rate).
   - Razer: Razer Synapse (Synapse Web is in beta). Open tools: OpenRazer (Linux), OpenRGB (lighting, also Windows).
   - SteelSeries: SteelSeries GG. Open tool: rivalcfg (also runs on Windows; built by reverse engineering, lists every supported model with its USB ID).
   - Corsair: iCUE. Open tools: ckb-next, OpenRGB.
   - ROCCAT, HyperX, ASUS, MSI, Cooler Master: their own apps; several are covered by libratbag and/or OpenRGB.
3. **Budget "gaming" mice built on shared chips.** Many brands buy the same chips: SinoWealth (`258A`, used by Glorious, Redragon and many more), Holtek (`04D9`, `1A81`), Pixart (`093A`), Sunplus (`1BCF`). Each brand ships its own app, and models differ even on the same chip. Open tools: libratbag's sinowealth driver, gloriousctl, mouse_m908 (Redragon). These change the mouse's memory, so a wrong command can break a mouse.
4. **Mice set up in the browser (WebHID).** Newer mice (for example ATK/VXE with ATK HUB, Finalmouse XPanel, Razer Synapse Web) are configured from a web page in Chrome or Edge. Firefox and Safari don't support WebHID. OpenMouse is an open-source browser control panel for several brands.
5. **Mice that need no software.** For example ZOWIE (BenQ): DPI and report rate are set with buttons on the mouse.

## What this means for the app

- The universal parts (detect, test, report rate, Windows settings) are built and work for any mouse. App-side remapping was built and then removed (2026-11-24): the app is meant to set things up and then close, not keep running.
- For each mouse, the Overview tab says which maker or chip it is, how its settings are normally changed, which tools can do it, and what this app can do. 33 makers and chips are listed by USB vendor ID (names checked in `usb.ids` version 2026.06.26).
- Hardware plugins are added one maker at a time, **only from documented or tested protocols**, never guessed commands. Good next candidates:
  1. Logitech HID++ 2.0 (biggest maker, best documented): DPI, report rate, battery.
  2. SteelSeries (rivalcfg documents each model).
  3. Lighting for many brands through OpenRGB's network SDK (OpenRGB runs on Windows; its SDK server listens on port 6742), so the app wouldn't need its own code per brand.
- Each new plugin needs a real mouse of that kind to test before release.

## Sources

- libratbag (gaming mouse configuration library and device list): https://github.com/libratbag/libratbag
- libratbag 0.18 release notes (drivers: hidpp20, steelseries, roccat, sinowealth, asus): https://lists.freedesktop.org/archives/input-tools/2024-September/001590.html
- Solaar HID++ feature list (0x2201 adjustable DPI, 0x8060 report rate): https://sources.debian.org/src/solaar/1.1.19-2/docs/features.md
- rivalcfg (SteelSeries, works on Windows): https://flozz.github.io/rivalcfg/
- OpenRGB SDK documentation (network protocol, port 6742): https://github.imc.re/CalcProgrammer1/OpenRGB/blob/master/Documentation/OpenRGBSDK.md
- OpenMouse (open-source WebHID control panel): https://github.com/OpenMouse-Project/openmouse
- ATK V HUB web driver instructions: https://www.atk.store/blogs/news/atk-v-hub-web-version-instructions
- gloriousctl (Glorious / SinoWealth mice, `258a:0036`): https://awesome.ecosyste.ms/projects/github.com%2Fenkore%2Fgloriousctl
- mouse_m908 (Redragon mice on Linux): https://github.com/dokutan/mouse_m908
- Linux kernel patch naming `0x258a` as the SinoWealth vendor ID: https://lore.kernel.org/r/20231106235557.8741-3-braye@fastmail.com
- redgear-a17-linux (Sunplus `1BCF:08A0` protocol, used for this app's plugin; facts only, no code copied): https://github.com/sh7vansh/redgear-a17-linux
- USB ID database (`usb.ids`, version 2026.06.26), systemd copy: https://raw.githubusercontent.com/systemd/systemd/main/hwdb.d/usb.ids
