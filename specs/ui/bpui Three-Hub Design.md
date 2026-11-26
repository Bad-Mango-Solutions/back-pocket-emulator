# bpui Three-Hub Design

## Document information

| Field | Value |
| --- | --- |
| Version | 1.1 |
| Date | 2026-11-26 |
| Status | Revised after review of [#797](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/pull/797). Docs only |
| Epic | [#742](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/742) |
| This issue | [#743](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/743) |
| Host API | [#734](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/734) |
| Browser head | [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) |
| Applies to | `bpui` (`BadMango.Emulator.UI`), `BadMango.Emulator.Theme` ([#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744)), Debug.UI theme adoption ([#753](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/753)), TextEditor resource keys ([#754](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/754)) |

This document is the source of truth for the `bpui` redesign. Implementation issues [#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744) through [#754](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/754) build against it. It supersedes the Styling and Theming section of the [Emulator UI Specification](../Emulator%20UI%20Specification.md). The older modules in that specification describe the pre-redesign shell and remain until [#747](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/747)–[#752](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/752) replace them.

Version 1.1 replaces the single-window hub bar from 1.0. Each running machine has its own window. The Instance manager, Platform settings, and the package manager are separate windows, opened from a menu.

No code and no XAML ship with this document. Mockups are static SVG under [`specs/ui/mockups/`](mockups/).

## 1. Purpose

`bpui` today is a sidebar of six equal buttons, a stubbed Machine Manager, and four placeholder pages, drawn with the stock VS Code palette on filled rounded cards. It has never connected to a machine. The redesign makes it a client of the host API ([#733](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/733), contracts in [#734](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/734)) with one visual language shared by `bpui`, Debug.UI, and the browser head.

The machine window is a monitor with a thin chrome. Speed and power are always at hand. Display mode, slots, and input are a menu away. The result should feel like a compact tool: warm, quiet, and specific. Regions are separated by hairlines and the spacing scale. Mango appears rarely enough that a running machine, the keyboard focus, and the single primary action are obvious at a glance.

## 2. Decisions

### 2.1 Three surfaces, three kinds of window

The product still has three hubs. They are not tabs in one window.

| Surface | Window | What it is |
| --- | --- | --- |
| Machine | One top-level window per running instance, built around that instance's monitor | Power, speed, the drives under the display, and the menus for everything else |
| Instance | The instance manager, a separate launcher window | Add an instance, configure it, see its state and connectivity, open its machine window, remove it |
| Platform | Platform settings, a separate window | App configuration, host-service connections, the media library, About |

Package management is a fourth window, opened from the same App menu. It is not a section inside Platform settings. Section 9 specifies it.

A surface is a `UserControl`. On the desktop each one is hosted in its own `Window`. The [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) browser head hosts the same controls and reaches them from the same menus. Section 15 lists what the browser reuses.

There is no hub switcher on the machine window. Machine, Instances, and Platform are not tabs.

### 2.2 The media library lives in Platform settings

The media library is a section of the Platform settings window.

It is shared across instances. A disk image is a host file the user owns, and any instance can insert it. Putting the catalog on an instance would hide it whenever that instance's window was closed, and would imply the file belongs to one machine.

A drive button picks an entry from the library (or a file the user supplies through `StorageProvider`) and passes it to the host control plane. Creating a blank image from a drive adds the new file to the library and inserts it. Creating one from the library adds the file and leaves every drive unchanged.

The library stores paths, display names, and the format and size the host reports. It does not store guest filesystem contents, and it does not bundle disk images.

### 2.3 What is common, and what is a menu

| Always in the machine chrome | In a menu or its own window |
| --- | --- |
| Speed throttle | Display mode (Mono, Green, Amber, NTSC, RGB) |
| Power menu | Scaling |
| Reset, as a quick button beside the power menu | Slots, input, and the machine-status summary |
| Drive buttons, one per drive, on the bottom chrome | Instance manager, Platform settings, package manager |
| Open Pocket and Closed Pocket keycaps | Debugger and editor |

Display mode is rare, so it has no segmented control in the chrome. Speed is common, so the throttle stays in the top chrome. The measured speed on the bottom strip is a reading, and the throttle is the control.

### 2.4 Visual language

Dark is the default. Neutrals are warm and near-black: `#141311` for the ground, `#1B1A17` for chrome, and `#24221E` for hover. The light theme is the same hue family on warm paper.

There is one accent, mango. It is used for keyboard focus, the one primary action on a surface, the running state, a selected control, and drive activity. The denim in the brand mark stays in the brand mark. There is no second accent and no gradient.

Hairlines (1px) and the spacing scale separate content. Controls use a 2px radius, panels and menus use 4px, and the monitor surface uses 0. Density is compact. Hit targets in the chrome are at least 28px tall.

Type is Inter for UI text and IBM Plex Mono for anything that is an address, a slot, a track, a block, a speed, or a stat. `bpui`, Debug.UI, and TextEditor share that one mono. Inter is supplied by `Avalonia.Fonts.Inter`. IBM Plex Mono is bundled with its OFL license by [#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744).

Period touches are limited to phosphor names in the View → Display menu, drive activity LEDs, monospace slot labels such as `S6 D1`, and the Open Pocket and Closed Pocket keycaps. CRT simulation, scanlines, and a bezel belong to the shader epic [#459](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/459), so this redesign draws the monitor as a flat field of the decoded signal.

### 2.5 Brand

`back-pocket-logo.png` at the repository root is the brand mark. Leave the file where it is. Do not redraw, recolor, crop, or replace it.

Use it in three places:

- The instance manager header, at 28px.
- The desktop window icon and, when [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) builds the browser head, the page icon.
- The About block in Platform settings, at 48px.

The accent was checked against that file on 2026-11-25. Sampling excluded the cream background and the near-black outline. Orange handle pixels (red above 170, green between 70 and 190, blue under 100, and red at least 40 above green) have a median of `#D98E37`. The most saturated quarter of those pixels averages `#ED882E`. The brightest handle highlights average `#F39135`. The pocket denim median is `#6591A9`.

`#F29A2E` sits in that highlight band and is the dark-theme accent. On `#141311` its contrast is 8.3:1, so it works as both a fill and as text. `#F29A2E` on the light paper `#F6F3EC` is 2.0:1, and the logo's own median `#D98E37` is 2.4:1 there. A 1px focus ring needs 3:1, and small text needs 4.5:1, so the light theme keeps the same hue (about 33°) and uses two values: `#AD732B` for fills, rings, and the running dot (3.6:1 on the paper, and `#1A1208` on that fill clears 4.5:1) and `#9B6017` for mango text (clears 4.5:1 on the paper). Both themes are specified in section 9.

### 2.6 Keycaps

`KeyCap` draws the Open Pocket and Closed Pocket glyphs from Josh's `pocket2-charset.rom` (embedded as `BadMango.Emulator.Devices.Resources.pocket2-charset.rom`). It draws those glyphs for the two modifier keys the guest still reads as pushbuttons. UI copy uses the pocket names.

The alternate character set in that ROM holds the glyphs:

| Glyph | Alternate-set code | Shape | Guest pushbutton |
| --- | --- | --- | --- |
| Closed Pocket | `$40` | Filled pocket: a flat welt, a nearly solid body, a pointed bottom, and one open scanline at the mouth | PB1, `$C062` |
| Open Pocket | `$41` | The same silhouette as an outline, with an open interior and a pointed bottom | PB0, `$C061` |

The rest of the alternate set (`$42`–`$5F`, and the mirrored range the character generator already uses) is MouseText: rules, arrows, a checker, a folder, and the other pictographs in that ROM. MouseText belongs on the monitor when the guest selects the alternate character set. Chrome does not use it.

`KeyCap` also renders word keys (`CTRL`, `SHIFT`, `RESET`, `F10`) in IBM Plex Mono. Those are type, not ROM glyphs.

The soft-switch behavior is unchanged from the [Keyboard Mapping Specification](../Keyboard%20Mapping%20Specification.md). Open Pocket held through reset is the cold-start chord the control plane already exposes. The keyboard spec's historical key names stay in that document; `bpui` shows the pocket names.

Mockups in this spec draw a geometric pocket (a flat welt, an open mouth, and a pointed body) as a stand-in. They do not trace ROM pixels.

### 2.7 Artwork

Chrome and mockups use the brand mark, the geometric keycap stand-in, hairlines, and type. They do not include Apple artwork, Apple logos, rainbow stripes, a beige computer body, a CRT frame, ROM bitmaps, or third-party images. Guest video inside `MonitorSurface` is the presentation stream from the host. Sample text in the mockups is original and is there to show phosphor color and type. Package icons are geometric marks drawn by the UI, not artwork shipped with a package.

## 3. Information architecture

```
App menu (on every window)
├── Instance manager
├── Platform settings
├── Packages
└── About

Machine window          one per running instance
├── Menu bar            App, View, Tools
├── Top chrome          name, keycaps, speed, power menu, Reset
├── MonitorSurface
└── Bottom chrome       measured speed, FPS, load, one button per drive
    └── Drive panel     a small panel for the drive that was clicked

Instance manager        launcher window
├── Instance list
└── Detail              name, profile, launch, connectivity, open, remove

Platform settings
├── App                 theme, paths, startup, logging
├── Host services
├── Media library
└── About

Package manager         its own window
├── Browse / Installed / Updates
├── Search and the package list
└── Details             description, versions, dependencies, actions

Slots, Input, Status    their own windows, opened from the machine's View menu
```

The six-item sidebar (Machine Manager, Storage, Display, Debug, Editor, Settings) goes away with [#751](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/751). Debug windows stay in Debug.UI. The editor stays the TextEditor control, opened from Tools, themed through the keys in section 12.

Launching the app with no machine running opens the instance manager. Opening an instance opens its machine window, or focuses that window if it is already open. Closing a machine window leaves the instance running until the power menu powers it off. Closing the instance manager does not close machine windows.

## 4. Navigation

Every window has an App menu. The machine window adds View and Tools. Nothing in the chrome navigates between hubs.

| Menu | Items |
| --- | --- |
| App | Instance manager, Platform settings, Packages, About. On a machine window, Close is the last item |
| View | Display, Scaling, Slots, Input, Status, New window |
| Tools | Attach debugger, Open editor |

Display is a submenu: Mono, Green, Amber, NTSC. A check marks the window's current choice. When the output is RGB, the submenu is the single item "RGB", checked and disabled. Scaling is Integer or Aspect. New window opens another machine window on the same instance, with its own display choice.

Shortcuts, honored only while guest input is not captured:

| Shortcut | Opens |
| --- | --- |
| Ctrl+Shift+I | Instance manager |
| Ctrl+, | Platform settings |
| Ctrl+Shift+P | Package manager |

The last set of open machine windows is an app-local setting. Drive-panel open state does not persist. Slots, input, and status window positions persist per instance id.

## 5. Machine window

Built by [#747](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/747). The view is a `UserControl` hosted in a `Window` whose title is the instance name.

### 5.1 Menu bar

A 32px bar on `ThemeBackgroundRaised`, with a hairline under it. The items are App, View, and Tools, Inter 13px, `ThemeForegroundMuted`. The open menu is a panel of `ThemeBackgroundRaised` with a 1px `ThemeHairlineStrong` border and a 4px radius. Menu rows are 28px. The row under the pointer uses `ThemeHover`. A checked row uses `ThemeAccentText`.

### 5.2 Top chrome

A 48px bar on `ThemeBackgroundRaised`, with a hairline under it. From leading to trailing:

1. Instance name, Inter 15px.
2. Open Pocket and Closed Pocket `KeyCap`s, reflecting PB0 and PB1.
3. Speed throttle, a segmented control: `½×`, `1×`, `2×`, `Max`. This sets the host throttle. It is separate from the measured speed on the bottom strip.
4. Power. A ghost button: a 6px dot, the state word, and a chevron. While running, the dot and the word "Running" use mango. While paused, the word is "Paused" in muted type and the dot is absent. While off, the word is "Off". Activating it opens the power menu under the button.
5. Reset. A ghost button. It sends the same control-plane reset as the menu item. It stays in the chrome because reset is common. It is disabled while the machine is off.
6. Capture. Idle label "Capture". While captured, the label is "Captured · F10" in `ThemeAccentText`. F10 is the default host key that releases capture. The input window can change the key. The chip shows the current key.

Reset while Open Pocket is down is the cold-start chord. The keycap shows the down state for as long as the host reports the button held.

There is no Pause button, Stop button, or display-mode control in this bar.

### 5.3 Power menu

The power menu replaces the separate Pause, Reset, and Stop buttons. Reset remains as the quick button in section 5.2 and is also listed in the menu. The menu shows only the actions the machine supports in its current state. An unsupported action is absent, not shown disabled.

The control plane's machine info carries a `PowerCapabilities` record. `PowerMenuViewModel` reads it when the instance attaches, when the profile changes, and whenever machine info changes. The view does not branch on a personality name.

| Flag | Meaning | Menu item |
| --- | --- | --- |
| `SoftPower` | The machine has a soft-power circuit | "Shut Down" |
| `SoftPower` clear | The machine has no soft-power circuit | "Power Off" |
| `Sleep` | The guest can sleep | "Sleep" |
| `HardReset` | The host can power-cycle this machine | "Hard Reset" |

Pause and Resume are host control, so they are always available while the machine is on. Reset is always available while the machine is on. Power On is the only item while the machine is off.

| State | Menu, top to bottom |
| --- | --- |
| Off | Power On |
| Running | Pause, then Sleep if `Sleep` is set, a hairline, Reset, Hard Reset if `HardReset` is set, a hairline, then Shut Down or Power Off |
| Paused | Resume, a hairline, Reset, Hard Reset if `HardReset` is set, a hairline, then Shut Down or Power Off. Sleep is omitted while paused |
| Shutting down | The chosen item reads "Shutting down…" and the other items are omitted until the host reports off or running |

If the host sends no `PowerCapabilities` record, the menu uses the conservative set: Pause or Resume, Reset, Hard Reset, and Power Off. That set can cut power without assuming a soft-power circuit.

Each item calls the control plane: Power On, Pause, Resume, Sleep, Reset, Hard Reset, Shut Down, Power Off. Shut Down asks the guest to power off through the soft-power path. Power Off cuts power. Hard Reset cycles power. Sleep enters the guest's sleep state.

Shut Down and Power Off ask for confirmation when the host reports a dirty drive: "Power off Workshop? A drive has unsaved changes." Hard Reset asks "Hard reset Workshop?" Pause, Resume, Sleep, Reset, and Power On do not ask. The quick Reset button follows the same rule as the menu's Reset: no confirmation.

The instance manager's Power button opens this same menu for the selected instance. There is one `PowerMenuViewModel` per instance, shared by the machine window and the manager.

### 5.4 The monitor

`MonitorSurface` ([#746](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/746)) fills the space between the top chrome and the bottom chrome. It is inset 16px on `ThemeBackground`. The field itself is `ThemeMonitor` (`#0C0B09`) in both themes, because the field is the signal, not chrome. A 1px `ThemeHairline` outlines the field. Integer or aspect-correct scaling, chosen in View → Scaling, letterboxes inside the field. Radius 0. No bezel, no scanlines, no drop shadow.

A second window on the same instance (View → New window) has its own display choice and its own capture state, and subscribes to the same presentation stream. Two windows on one composite source may show Green and Amber at the same time. An RGB source passes the finished frame through and locks Display.

A drag onto the monitor inserts into the drive the host marks as the boot drive. If the host marks none, the drop is refused and the bottom strip says "Choose a drive."

The light theme does not lighten the monitor field.

### 5.5 Display mode

View → Display sends a monitor choice for this window. Where that choice is decoded is [#738](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/738). The chrome does not show Mono, Green, Amber, NTSC, or RGB.

| Output the host reports | Display submenu |
| --- | --- |
| Composite | Mono, Green, Amber, NTSC. One is checked |
| RGB | "RGB", checked and disabled |

The phosphor values in section 9.1 are the menu's content colors. They are not chrome tokens.

### 5.6 Bottom chrome

A 36px bar on `ThemeBackgroundRaised` with a hairline above it. IBM Plex Mono at 12px for the figures, Inter 11px for the captions.

| Item | Source |
| --- | --- |
| Measured speed, `100%` | Control plane, distinct from the throttle |
| Frame rate, `59.9 FPS` | Presentation stream |
| One drive button per drive | Control plane topology and activity |
| Load | `FileLoadProgress` while a load is in progress. At rest the strip omits it |

A drive button is a 28px ghost chip: a 6px activity LED and the slot label in IBM Plex Mono (`S6 D1`). The LED is `ThemeHairlineStrong` when idle and mango while the host reports activity. The open drive's label uses `ThemeAccentText`. An error drive uses `ThemeDanger` for the label. The button does not show the image name. That name is in the panel.

The UI does not hard-code how many buttons appear. The host's topology is what keeps these rules intact, and tests in [#748](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/748) assert them:

- A Liron profile has two UniDisk 3.5 drives and no 5.25-inch drive.
- On the IIgs, slot 5 is the SmartPort chain, slot 6 is the Disk Port, and 5.25-inch drives sit at the end of the chain.
- On the IIc, the external 5.25-inch drive is S6,D2, and the 3.5 ROM puts UniDisk 3.5 drives ahead of it.

The mockups use `S6 D1`, `S6 D2`, and `S5 D1` as sample labels a host might report.

### 5.7 Drive panel

Clicking a drive button slides a small panel up from that button. The panel is 300px wide, anchored to the button, and overlays the bottom corner of the monitor. It does not push the monitor and it is not a side column. A second click, Esc, or clicking another drive closes it. Only one drive panel is open.

The panel is `ThemeBackgroundRaised` with a 1px `ThemeHairlineStrong` border and a 4px radius. It holds, for that drive only:

- The slot label and the activity LED.
- The image name, or "Empty".
- A mono meta line: track (`T $12`) or block (`B 0042`), and `WP` when the image is write-protected.
- Eject, Write-protect, Library, and New image.

Motion is 160ms, ease-out, rising from the button. When the OS asks for reduced motion, the panel snaps.

| State | LED on the button | Panel | Actions |
| --- | --- | --- | --- |
| Empty | Idle | "Empty" | Library, New image |
| Inserted | Idle, mango while active | Image name, track or block | Eject, Write-protect, Library, New image |
| Write-protected | Same as inserted | Image name, track or block, `WP`. Write-protect reads as on | Eject, Library. The host refuses writes |
| Busy | Mango, steady | Image name, "In use" | Every action disabled until the host allows a change |
| Error | Idle, label in `ThemeDanger` | The name the user tried, and the host's message in `ThemeDanger` | Eject, Retry |

Library lists library entries whose format the drive reports it can take, then a "Choose file…" row that uses `StorageProvider`. A drag onto the button inserts there. New image offers only the formats and sizes that drive reports. The new file is added to the media library and inserted. Eject unmounts through the control plane. A dirty image asks for confirmation. Write-protect toggles the host flag. Busy covers a motor-on drive, an in-flight mount, and any other condition the host flags as refusing mutation. The actions dim to 40% opacity and do not take clicks. Retry repeats the last mount.

Only user-supplied files and blank images appear. The panel never offers a bundled disk or a ROM.

The drive does not own the catalog. Library reads the media library in Platform settings. A file chosen through `StorageProvider` is added to the library and then mounted. Removing a library entry does not eject a drive that currently has it mounted.

### 5.8 Slots, input, and status

These open their own windows from the View menu. They are not buttons on the machine chrome and they do not slide over the monitor.

Slots ([#749](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/749)) is a slot strip for S1–S7 and aux, each row a slot label in IBM Plex Mono, the card name, and that card's settings. Below the strip: memory, CPU, and firmware source. Firmware choices the host may offer are clean-room and user-supplied upload. While the instance is running the window is read-only, and a faint line reads "Power the machine off to change slots." Edits that need a restart carry the faint line "Takes effect on the next power-on." Edits go through the control plane.

Input ([#750](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/750)) shows the active mapping from the Keyboard Mapping Specification as a list of host key to guest key. Open Pocket and Closed Pocket are `KeyCap`s bound to the host keys the map assigns (the default map uses Left Alt and Right Alt). A ghost action "Paste clipboard" injects text through the input plane. Joystick and paddles show the host gamepad, or mouse-as-joystick, plus deadzone and calibration. Mouse shows capture mode and the release key. The window does not edit map files, and it does not implement the AppleMouse card ([#451](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/451)).

Status is read-only: CPU, clock, and a scheduler summary from the control plane's machine info. Debugger windows stay in Debug.UI.

## 6. Instance manager

Built by [#751](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/751). The view is a `UserControl` in its own window. It replaces `MachineManagerView`, `MachineManagerViewModel`, the sidebar, and `PlaceholderView`.

The menu bar is the App menu. The header is 48px: the brand mark at 28px, "Instances" in Inter 20px, and one primary button, "Add instance".

Below the header, a hairline splits a 480px list from the detail. List rows are 72px, separated by hairlines. The selected row carries a 2px mango bar on its leading edge. The row background stays `ThemeBackground`.

Each row shows:

- A 96×54 still from the latest presented frame, on `ThemeMonitor`, refreshed about once a second. The list does not open a second live surface per row.
- The instance name, Inter 15px.
- The model, the connectivity kind, and the run state. "Running" uses `ThemeAccentText` and the 6px dot. "Paused", "Off", and a remote "Reconnecting" use muted type.

The detail pane is the selected instance, or a create form after "Add instance".

- Name.
- Profile, chosen from the profiles the control plane lists.
- Launch options. "Start paused" is the one option this spec requires. Further options wait until the control plane exposes them.
- Connectivity, a segmented control: In-process ([#735](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/735)), Local host ([#736](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/736)), Remote ([#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737)). Under it, a mono health line: "Loopback · healthy", "Local host · disconnected", "remote.example · reconnecting", and the same shape for a refusal. The remote path does not assume a trusted localhost; auth and the firmware rule are [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737).
- Open. A ghost button that opens or focuses the machine window.
- Power. The same power menu as section 5.3, for this instance.
- Remove. A danger-text button "Remove instance". Activating it replaces the button with the line "Remove Workshop? Disk images stay in the library." and two actions, danger "Remove" and ghost "Cancel".

Hardware detail stays on the machine window and its View-menu windows. Packages stay in the package manager.

Every action goes through the host API. The list is the host's instance list. The Phase 1 sample profiles in `MachineManagerViewModel` are deleted with that view model.

A surface shows at most one filled primary. "Add instance" is that button. While a create form is open, "Create" is the filled button and "Add instance" becomes a ghost.

## 7. Platform settings

Built by [#752](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/752). The view is a `UserControl` in its own window. It replaces the Settings window and its pages, except the package screens, which section 8 takes.

The menu bar is the App menu. The header is the word "Platform" in Inter 20px. The content is one column, inset 32px, sections stacked. A section title is Inter 20px. A hairline and 24px of space separate sections. There is no card around a section.

An option with nothing behind it is omitted. Anonymous usage data and update checks are omitted until [#657](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/657) exists. The current General settings flags for telemetry and update checks are not migrated.

### 7.1 App

Theme (Dark, Light, System) as a segmented control. Library path, startup ("Reopen the last machine windows"), and logging level. Scanlines and CRT effects are [#459](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/459) and have no control here. Display mode and scaling live on the machine window's View menu.

A row "Packages" with a ghost button "Open package manager" is the way from this window to section 8. The row does not list packages.

### 7.2 Host services

One row per service, a hairline between rows, the state as a word ("Connected" or "On" in foreground, "Off" in faint), and a ghost Configure. A connected service does not use mango. Mango on this window is the primary action and focus.

| Service | Follows | Plane |
| --- | --- | --- |
| Printer and serial | [#449](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/449) | Host service. TCP bridge, print to PDF or PNG |
| Network | [#447](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/447) | Host service |
| Host folder as a ProDOS volume | [#455](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/455) | Host service |
| Clipboard paste | Input plane | Enables the paste action in the input window |
| Host gamepads | [#451](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/451) | Input plane, device list for the input window |
| Audio out | [#691](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/691) | Presentation plane. The toggle selects the sink for the PCM stream. The window does not call `IAudioOutput.Play` |

Printer, serial, network, and the folder bridge sit outside the three [#734](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/734) planes. They are enabled through the host's service-connection surface and the module permission model, which [#752](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/752) adds beside those planes. Toggles do not take a reference to a device type.

### 7.3 Media library

The section title is "Media library". The line under it reads "Shared by every instance. Drive panels insert from here."

Rows are display name, size, and format, in IBM Plex Mono for the size. Actions are Add files (`StorageProvider` and drag-and-drop), New image (format and size from what the host can create, with no drive attached), and Remove. Remove is refused with the host's message when a drive still has the image mounted; the faint line names the instance.

### 7.4 About

The brand mark at 48px, "Back Pocket" at 20px, and the version, build, and runtime in IBM Plex Mono. License and source links stay. The block shows the same mark the window icon uses.

## 8. Package manager

The package manager is its own window, opened from the App menu or from the Packages row in Platform settings. It is the screen [#752](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/752) uses for modules, clean-room firmware, and device packages ([#473](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/473), [#474](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/474), [#655](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/655), [#656](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/656), [#658](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/658)). The package format and the registry stay in the [#473](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/473) epic. This window is the client of that registry.

The layout follows the shape of the Visual Studio NuGet package manager and the CurseForge addon manager: a tab strip, a search field, a list, and a details pane.

### 8.1 Chrome

The menu bar is the App menu. The header is "Packages" in Inter 20px, with a search field trailing. Under the header, three text tabs: Browse, Installed, Updates. The active tab uses `ThemeAccentText` and a 2px underline. Updates shows a muted count when the registry reports updates. The search filters the active tab. A faint line under the tabs reads "Clean-room modules, firmware, and device packages."

The window never offers Apple or third-party ROMs. A package the registry would identify as one of those is not listed.

### 8.2 List

The list fills the leading half of the window. Rows are 56px, separated by hairlines. The selected row carries a 2px mango bar on its leading edge.

Each row shows:

- A 32px geometric icon, drawn by the UI from the package kind (module, firmware, device). The icon is a hairline mark on `ThemeBackgroundSunken`. It is not a bitmap from the package and not a third-party image.
- The package name, Inter 13px.
- The version and the author, IBM Plex Mono 12px for the version and Inter 12px muted for the author.
- A faint word when it helps the tab: "Installed", or "Update" on the Updates tab.

### 8.3 Details

The details pane is the trailing half.

- Name, Inter 20px, and under it the author and the selected version.
- A description paragraph.
- Versions, a short list. The installed version, when there is one, uses `ThemeAccentText`.
- Dependencies, IBM Plex Mono, one name and version constraint per line.
- One action. Install is the filled primary when the package is not installed. Update is the filled primary when the registry reports a newer version. Uninstall is a danger-text button when the package is installed and no update is waiting. A package with both an install and an uninstall shows Install or Update as the primary and Uninstall as the danger button. The pane still has one filled button.

Install, update, and uninstall go through the module permission model. They do not take a reference to a device type.

## 9. Tokens

[#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744) ships these from `BadMango.Emulator.Theme`, a resources-only assembly whose only dependency is Avalonia. `bpui`, Debug.UI, and the browser head reference it. TextEditor does not.

FluentTheme stays the behavior base. The token dictionaries override Fluent's resource keys, including `SystemAccentColor` (from `ThemeAccent`) and the control corner radius (from `ThemeRadiusControl`). Light is a swap of the color dictionary. Views use the keys. Views do not contain hex literals. [#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744) includes a check that fails the build when `bpui` view XAML contains one.

### 9.1 Color

`ThemeMonitor` is the same value in both dictionaries.

| Key | Dark | Light | Use |
| --- | --- | --- | --- |
| `ThemeBackground` | `#141311` | `#F6F3EC` | Window ground |
| `ThemeBackgroundRaised` | `#1B1A17` | `#FFFCF7` | Chrome, menus, the drive panel |
| `ThemeBackgroundSunken` | `#100F0D` | `#EBE6DC` | Fields, empty thumbnails, the inset around the monitor |
| `ThemeHover` | `#24221E` | `#EFEAE2` | Ghost, menu, and row hover |
| `ThemeMonitor` | `#0C0B09` | `#0C0B09` | Monitor field. Guest video draws on top |
| `ThemeForeground` | `#E8E2D6` | `#1C1915` | Primary text. 14:1 on dark, 15:1 on light |
| `ThemeForegroundMuted` | `#9C958A` | `#5C564C` | Secondary text. At least 6:1 |
| `ThemeForegroundFaint` | `#8E8278` | `#70685C` | Captions. At least 4.5:1, so 11px labels stay readable |
| `ThemeHairline` | `#34302A` | `#E4DCD0` | 1px dividers |
| `ThemeHairlineStrong` | `#4A453C` | `#C9C0B2` | Control borders, keycap edges, idle LED |
| `ThemeAccent` | `#F29A2E` | `#AD732B` | Focus ring, primary fill, running dot, activity LED, selected toggle thumb |
| `ThemeAccentText` | `#F29A2E` | `#9B6017` | Mango text, the checked menu row, the active tab underline |
| `ThemeOnAccent` | `#1A1208` | `#1A1208` | Text on an accent fill. 8.3:1 on the dark accent, 4.6:1 on the light accent |
| `ThemeSelection` | `#453117` | `#E2CFB6` | Solid selection fill, pre-blended so views do not use alpha |
| `ThemeDanger` | `#E15A4A` | `#B83E32` | Error text and the destructive label. At least 4.5:1 |

Phosphor colors are monitor-content values inside `MonitorSurface`, chosen from View → Display. They are not chrome tokens and not Fluent overrides:

| Display | Field text |
| --- | --- |
| Mono | `#E6E2D8` |
| Green | `#B7E39A` |
| Amber | `#F0C36A` |
| NTSC | The decoded frame, untinted |
| RGB | The finished frame, the Display item locked |

`ThemeDanger` is a functional color for errors and the destructive confirm. It is not a second accent: it does not fill a toolbar, a selection, or a running state. A destructive button is ghost-shaped with danger text, not a filled red button.

### 9.2 Type

`ThemeFontUi` is Inter. `ThemeFontMono` is IBM Plex Mono.

| Key | Size | Line height | Use |
| --- | --- | --- | --- |
| `ThemeFontSizeMicro` | 11 | 14 | Captions, keycap word-keys, status captions |
| `ThemeFontSizeCaption` | 12 | 16 | Secondary UI, mono figures |
| `ThemeFontSizeBody` | 13 | 18 | Default UI, menus |
| `ThemeFontSizeBodyLarge` | 15 | 20 | Instance name in the machine chrome, panel titles |
| `ThemeFontSizeTitle` | 20 | 28 | Window titles, section titles |
| `ThemeFontSizeDisplay` | 28 | 36 | Reserved |

Mono figures use `ThemeFontSizeCaption` unless a panel is showing a single address, which may use `ThemeFontSizeBody`.

### 9.3 Spacing

A 4pt scale. Keys: `ThemeSpace1` 4, `ThemeSpace2` 8, `ThemeSpace3` 12, `ThemeSpace4` 16, `ThemeSpace5` 24, `ThemeSpace6` 32.

Chrome padding is 16px (`ThemeSpace4`). The gap between a caption and its value is 4px. Section padding in Platform settings is 32px. Rows inside a panel use 12px of vertical padding.

### 9.4 Radius

| Key | Value | Use |
| --- | --- | --- |
| `ThemeRadiusControl` | 2 | Buttons, fields, segmented controls, thumbnails, package icons. Overrides Fluent `ControlCornerRadius` |
| `ThemeRadiusPanel` | 4 | Menus, the drive panel, secondary windows |
| `ThemeRadiusKeycap` | 3 | `KeyCap` |
| `ThemeRadiusMonitor` | 0 | `MonitorSurface` |

### 9.5 Focus, density, motion

The focus ring is a 1px `ThemeAccent` stroke, 2px outside the control. Every themed control in [#745](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/745) shows it. Chrome controls are at least 28px tall. The drive panel moves 160ms ease-out, and it snaps when the OS requests reduced motion. Menus open without a travel animation.

### 9.6 How a surface is built

A surface is ground, hairlines, type, and at most one accent fill. The practices that keep it that way:

- Separators are 1px hairlines.
- The selected list row is a 2px leading bar in `ThemeAccent`.
- The active tab and a checked menu row are accent text. The active tab also has a 2px underline.
- The on state of a toggle is a mango thumb on a strong-hairline track.
- Buttons are primary (accent fill, on-accent text), ghost (hairline, foreground text), or danger (hairline, danger text).
- A control that carries an icon also carries a text label or a tooltip. Drive buttons carry the slot label. Package rows carry the package name.

Filled cards, drop shadows, gradients, a second accent, a rainbow, a beige body, and a CRT frame are outside this language.

## 10. Controls

| Control | Issue | Role |
| --- | --- | --- |
| `MonitorSurface` | [#746](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/746) | Presents one window's stream, captures input, accepts a drop on the boot drive |
| Drive button and drive panel | [#748](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/748) | One reported drive: a bottom-chrome chip, and the small panel for insert, eject, library, create, and write-protect |
| Bottom strip | [#747](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/747) | Measured speed, FPS, load, the drive buttons |
| Power menu | [#747](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/747) | The capability-filtered power actions, plus the Reset quick button |
| Segmented control | [#745](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/745) | Speed throttle, connectivity, theme |
| `KeyCap` | [#750](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/750) | Open Pocket, Closed Pocket, and mono word keys |

[#745](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/745) also themes Button (primary, ghost, danger), ToggleButton, ListBoxItem, TextBox, ComboBox, ScrollBar, TabStrip, and ToolTip. Those themes use the tokens above. Domain controls (`MonitorSurface`, the drive button) are not part of [#745](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/745).

The segmented control is an inline group, 28px tall, radius 2, with a hairline around the group and between segments. The selected segment is `ThemeAccentText` with no fill. An unselected segment is `ThemeForegroundMuted`. A disabled segment is `ThemeForegroundFaint`. Arrow keys move the selection. Display mode does not use this control.

`KeyCap` is 28×28, radius 3, fill `ThemeBackgroundRaised`, border `ThemeHairlineStrong`. The up state draws the glyph in `ThemeForegroundMuted`. The down state draws it in `ThemeForeground` and the border in `ThemeAccent`. Disabled draws `ThemeForegroundFaint`. The glyph is the ROM bitmap for `$40` or `$41`, scaled to the cap with integer scaling and centered. Word keys set the mono string instead of a glyph.

The power menu and the drive panel use the menu and panel rules in sections 5.1 and 5.7. They are not card containers.

## 11. Theme assembly

`src/BadMango.Emulator.Theme` contains:

- `Tokens.Colors` dark and light dictionaries, the keys in section 9.1.
- `Tokens.Type`, the families and the scale in section 9.2.
- `Tokens.Spacing`, section 9.3.
- `Tokens.Radius`, section 9.4.

IBM Plex Mono ships in the assembly with its OFL license. Inter continues to come from `Avalonia.Fonts.Inter`. No other font is bundled.

`bpui` includes the theme and retires `Resources/Styles/ColorPalette.axaml`. Debug.UI takes the same reference in [#753](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/753) and drops hex literals and its three mono stacks. `emudbg --headless` still loads no Avalonia.

One assembly against the direction of [#477](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/477) is the point of [#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744): three consumers share one token set. TextEditor stays a standalone library and does not reference the assembly.

## 12. TextEditor resource keys

[#754](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/754) exposes these keys from TextEditor, with neutral defaults so an unhosted editor looks as it does today. TextEditor takes no dependency on `BadMango.Emulator.Theme`. The TextMate theme, hard-coded `DarkPlus` today, becomes a settable property and stays `DarkPlus` when the host sets nothing.

| TextEditor key | Neutral default | Host maps it to |
| --- | --- | --- |
| `TextEditorBackground` | The editor's current background | `ThemeBackgroundSunken` |
| `TextEditorForeground` | The editor's current foreground | `ThemeForeground` |
| `TextEditorGutter` | The editor's current gutter | `ThemeBackgroundRaised` |
| `TextEditorSelection` | The editor's current selection | `ThemeSelection` |
| `TextEditorCaret` | The editor's current caret | `ThemeAccent` |
| `TextEditorFontFamily` | The editor's current mono stack | `ThemeFontMono` |

[#753](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/753) applies the host column. [#754](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/754) only adds the keys and the settable TextMate theme.

## 13. View models and the host API

[#734](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/734) defines three planes. Clients address a machine by id. Calls are safe from the client thread. The machine thread stays exclusive. A view model references the contracts, not `IMachine`, `VideoDevice`, or `IAudioOutput`.

| Plane | What a view model may do through it |
| --- | --- |
| Control | Power On, Pause, Resume, Sleep, Reset, Hard Reset, Shut Down, Power Off, state, `PowerCapabilities`, clock, throttle, instances, profiles, disks (`MountDisk`, `EjectDisk`, `ListDisks`), attach and detach the debugger, machine info |
| Presentation | Subscribe to video frames or a raw composite signal, send a monitor choice, read frame timing, subscribe to the PCM audio stream. The client does not call `IAudioOutput.Play`. Decode location follows [#738](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/738) |
| Input | Keyboard down and up with modifiers, joystick, paddles, buttons, mouse move and buttons, capture and release |

The debugger extension of the control plane (breakpoints, watches, memory, disassembly) belongs to Debug.UI. `bpui` uses attach and detach, plus the machine-info summary. `PowerCapabilities` is part of that summary. Section 5.3 is the rule for which menu rows it produces.

### 13.1 Target view models

| View model | Window | Contract |
| --- | --- | --- |
| `AppMenuViewModel` | Every window | None of the three planes. Opens the instance manager, Platform settings, the package manager, and About |
| `MachineWindowViewModel` | Machine | Control: state, clock, throttle, dirty-drive flag, attach and detach debugger, `PowerCapabilities` for the menu. Addresses one machine id. Display choice is delegated to `MonitorViewViewModel` |
| `PowerMenuViewModel` | Machine, and the Power button on the instance manager | Control: the actions in section 5.3, filtered by `PowerCapabilities` and run state. One instance per machine, shared by both windows |
| `MonitorViewViewModel` | Machine | Presentation: frames or raw composite signal, the display choice from the View menu, frame timing. Input: keys, mouse, joystick and paddles while captured, capture and release. One window |
| `DriveStripViewModel` | Machine | Control: `ListDisks` topology, activity, boot-drive id |
| `DriveBayViewModel` | Machine | Control: `MountDisk`, `EjectDisk`, write-protect, create-blank capability, busy and error, for one drive. Library reads `MediaLibraryViewModel`, then mounts |
| `SlotsWindowViewModel` | Slots | Control: slot map, memory, CPU, firmware source. Writes only while the machine is off |
| `InputWindowViewModel` | Input | Input: active map (read), pocket-key state, clipboard paste as key injection, gamepad and paddle calibration, mouse capture mode, release key |
| `MachineStatusViewModel` | Status | Control, read-only: CPU, clock, scheduler summary from machine info |
| `InstanceManagerViewModel` | Instance manager | Control: list, add from profile, remove, open the machine window |
| `InstanceRowViewModel` | Instance manager | Control: state, model, connectivity, health. Presentation: a still of the latest frame, about once a second |
| `InstanceConfigViewModel` | Instance manager | Control: name, profile, launch options, connectivity kind |
| `ConnectivityViewModel` | Instance manager | Control: transport kind and health for in-process, local host, and remote. The remote transport is [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737); the view model still calls the [#734](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/734) control contracts |
| `PlatformSettingsViewModel` | Platform settings | None of the three planes. Which section is in view is window state |
| `AppConfigViewModel` | Platform settings | None of the three planes. Theme, library path, startup, and logging are app-local settings |
| `HostServicesViewModel` | Platform settings | Input for clipboard paste and host gamepads. Presentation for the audio-out sink. Printer, serial, network, and the host folder are the service-connection surface beside the three planes |
| `MediaLibraryViewModel` | Platform settings | None of the three planes. Catalog only. A drive panel turns an entry into a control-plane `MountDisk` |
| `AboutViewModel` | Platform settings | None. Brand mark, version, build |
| `PackageManagerViewModel` | Package manager | None of the three planes. Module permission model ([#473](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/473)). Browse, Installed, and Updates are views over that registry |

### 13.2 Current view models

These are the view models in `src/BadMango.Emulator.UI/ViewModels` today, and where each one goes. None of them call the host API yet. The stub data in `MachineManagerViewModel` is not a contract and is not migrated.

| Current | Disposition | Takes its place |
| --- | --- | --- |
| `MainWindowViewModel` | Replaced | `MachineWindowViewModel` for a machine, and the three other window view models |
| `NavigationItemViewModel` | Deleted with the six-button sidebar | The App menu. There is no hub switcher |
| `PlaceholderViewModel` | Deleted | The machine window, the instance manager, and Platform settings |
| `MachineManagerViewModel` | Deleted ([#751](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/751)) | `InstanceManagerViewModel` |
| `MachineInstanceViewModel` | Replaced | `InstanceRowViewModel` |
| `MachineProfileViewModel` | Replaced | Profile data on `InstanceConfigViewModel`, from the control plane |
| `PopOutWindowViewModel` | Replaced | The Slots, Input, and Status windows, and View → New window |
| `SettingsWindowViewModel` | Deleted | `PlatformSettingsViewModel` |
| `SettingsPageViewModelBase` | Deleted | The section view models below |
| `GeneralSettingsViewModel` | Replaced. Telemetry and update checks are dropped | `AppConfigViewModel` |
| `LibrarySettingsViewModel` | Split | Paths on `AppConfigViewModel`, images on `MediaLibraryViewModel` |
| `DisplaySettingsViewModel` | Replaced. Scanlines and CRT palette controls are not migrated | Display and scaling on `MonitorViewViewModel`, from the View menu |
| `InputSettingsViewModel` | Replaced. Becomes per instance | `InputWindowViewModel` |
| `DebugSettingsViewModel` | Split | Attach on `MachineWindowViewModel`; log level on `AppConfigViewModel` |
| `EditorSettingsViewModel` | Replaced by resource keys | Section 12. Host mapping is [#753](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/753) |
| `AboutViewModel` | Kept, moved into Platform settings | `AboutViewModel` |

## 14. What the browser head reuses

[#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) is an Avalonia browser client on the same host API. It reuses views and view models. It does not get a second visual language, and it does not get a tab strip across the machine view.

Reused as `UserControl`s, with the view models in section 13.1:

- The machine window, including `MonitorSurface`, the bottom strip, the drive buttons, and the drive panel.
- The power menu.
- The instance manager, including the connectivity segment. Remote is the browser's ordinary case; in-process remains available when the page hosts a loopback.
- Platform settings, including App, Host services, Media library, and About.
- The package manager.
- TextEditor, when Tools opens it, themed through section 12.

On the desktop each of those controls is a `Window`. In the browser the App menu shows one of them at a time, and the drive panel and the power menu stay overlays on the machine view. The browser does not put the instance manager or Platform settings inside the machine view as tabs.

Desktop only:

- View → New window, and the Slots, Input, and Status windows. In the browser those are further views reached from the View menu, one at a time.
- Debug.UI windows. The browser slice attaches, presents frames, plays the PCM stream, and sends keyboard, gameport, and mouse input. Debugger windows stay on the desktop ([#753](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/753)).

Both hosts open files through `StorageProvider` and drag-and-drop. Media is user-supplied. The firmware rule from [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) stands: the host does not serve Apple ROMs, and a remote machine boots clean-room firmware or firmware the user uploaded.

Input latency on the network path is the one [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) documents. The local path stays on its own transport. The view models do not branch on that choice; the transport behind the contracts does.

## 15. Mockups

Static SVG, drawn for this spec. They reference `back-pocket-logo.png` by relative path and do not copy it. The pocket marks on the keycaps are geometric stand-ins, described in section 2.6. Guest text on the monitor is original sample text so the phosphor and the mono face are visible. Display mode is absent from the machine chrome on purpose. It lives in View → Display, specified in section 5.5.

| File | What it shows |
| --- | --- |
| [`mockups/machine-window.svg`](mockups/machine-window.svg) | The machine window, dark. Menu bar, speed, power, Reset, drive buttons |
| [`mockups/machine-window-light.svg`](mockups/machine-window-light.svg) | The same window in the light theme. The monitor field stays `#0C0B09` |
| [`mockups/power-menu.svg`](mockups/power-menu.svg) | The power menu open for a machine that reports soft power, sleep, and hard reset |
| [`mockups/drive-panel.svg`](mockups/drive-panel.svg) | One drive's panel slid up from its bottom-chrome button |
| [`mockups/instance-manager.svg`](mockups/instance-manager.svg) | The instance manager window |
| [`mockups/platform-settings.svg`](mockups/platform-settings.svg) | Platform settings: app, host services, media library, About |
| [`mockups/package-manager.svg`](mockups/package-manager.svg) | The package manager: Browse, search, list, details |

## 16. Out of scope

- Code, XAML, and the theme assembly (those are [#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744) onward).
- CRT and NTSC shaders ([#459](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/459)).
- Disk format implementation ([#219](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/219)) and new drive hardware.
- The package format and registry ([#473](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/473)).
- Rewiring production `bpui` onto the host API ([#735](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/735) gates the information-architecture rebuild).
- A cloud product: billing, tenancy, and orchestration stay on [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737)'s epic.

## 17. Who builds what

| Issue | Builds |
| --- | --- |
| [#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744) | Section 9 and section 11 |
| [#745](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/745) | Button, toggle, segmented control, list row, field, scroll, tab, tooltip |
| [#746](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/746) | `MonitorSurface`, section 5.4 |
| [#747](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/747) | Machine window chrome, power menu, bottom strip |
| [#748](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/748) | Drive buttons and the drive panel, section 5.6 and section 5.7 |
| [#749](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/749) | Slots window |
| [#750](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/750) | Input window and `KeyCap` |
| [#751](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/751) | Instance manager, deletion of the Machine Manager stub and placeholders |
| [#752](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/752) | Platform settings and the package manager window |
| [#753](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/753) | Debug.UI adopts the theme and maps section 12 |
| [#754](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/754) | TextEditor keys in section 12, with neutral defaults |
