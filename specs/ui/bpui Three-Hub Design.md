# bpui Three-Hub Design

## Document information

| Field | Value |
| --- | --- |
| Version | 1.0 |
| Date | 2026-11-25 |
| Status | Accepted design, docs only |
| Epic | [#742](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/742) |
| This issue | [#743](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/743) |
| Host API | [#734](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/734) |
| Browser head | [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) |
| Applies to | `bpui` (`BadMango.Emulator.UI`), `BadMango.Emulator.Theme` ([#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744)), Debug.UI theme adoption ([#753](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/753)), TextEditor resource keys ([#754](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/754)) |

This document is the source of truth for the `bpui` redesign. Implementation issues [#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744) through [#754](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/754) build against it. It supersedes the Styling and Theming section of the [Emulator UI Specification](../Emulator%20UI%20Specification.md). The older modules in that specification describe the pre-redesign shell and remain until [#747](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/747)–[#752](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/752) replace them.

No code and no XAML ship with this document. Mockups are static SVG under [`specs/ui/mockups/`](mockups/).

## 1. Purpose

`bpui` today is a sidebar of six equal buttons, a stubbed Machine Manager, and four placeholder pages, drawn with the stock VS Code palette on filled rounded cards. It has never connected to a machine. The redesign makes it a client of the host API ([#733](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/733), contracts in [#734](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/734)) organized as three hubs, with one visual language shared by `bpui`, Debug.UI, and the browser head.

The result should feel like a compact tool: warm, quiet, and specific. Regions are separated by hairlines and the spacing scale. The monitor is the largest thing on the Machine hub. Mango appears rarely enough that a running machine, the keyboard focus, and the single primary action are obvious at a glance.

## 2. Decisions

### 2.1 Three hubs

| Hub | What it is | Replaces |
| --- | --- | --- |
| Machine | One running instance. The monitor is the hub. Chrome sits on the top and bottom edges. State and control panels slide out or pop out. Drive bays select an image, insert, eject, and create a blank image. | The display placeholder, and the per-instance half of today's Machine Manager |
| Instance | The host-to-guest control surface: add an instance, configure it, control power, run, pause, and reset, choose how it is connected, and remove it. | `MachineManagerView` |
| Platform | The app and the host: app configuration, package management, connections to host OS services, and the media library. | Today's Settings pages |

A hub is a `UserControl`. On the desktop the shell hosts it in a window. The [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) browser head hosts the same control. Section 14 lists which views the browser reuses.

### 2.2 The media library lives in the Platform hub

The media library is a Platform hub section.

It is shared across instances. A disk image is a host file the user owns, and any instance can insert it. Putting the catalog on an instance would hide it whenever that instance was closed, and would imply the file belongs to one machine.

Drive bays are the per-drive control. They pick an entry from the library (or a file the user supplies through `StorageProvider`) and pass it to the host control plane. Creating a blank image from a bay adds the new file to the library and inserts it. Creating one from the library adds the file and leaves every bay unchanged.

The library stores paths, display names, and the format and size the host reports. It does not store guest filesystem contents, and it does not bundle disk images.

### 2.3 Visual language

Dark is the default. Neutrals are warm and near-black: `#141311` for the ground, `#1B1A17` for chrome, and `#24221E` for hover. The light theme is the same hue family on warm paper.

There is one accent, mango. It is used for keyboard focus, the one primary action on a surface, the running state, a selected control, and drive activity. The denim in the brand mark stays in the brand mark. There is no second accent and no gradient.

Hairlines (1px) and the spacing scale separate content. Controls use a 2px radius, panels and popped windows use 4px, and the monitor surface uses 0. Density is compact. Hit targets in the chrome are at least 28px tall.

Type is Inter for UI text and IBM Plex Mono for anything that is an address, a slot, a track, a block, a speed, or a stat. `bpui`, Debug.UI, and TextEditor share that one mono. Inter is supplied by `Avalonia.Fonts.Inter`. IBM Plex Mono is bundled with its OFL license by [#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744).

Period touches are limited to phosphor names in the monitor picker, drive activity LEDs, monospace slot labels such as `S6 D1`, and the Open Pocket and Closed Pocket keycaps. CRT simulation, scanlines, and a bezel belong to the shader epic [#459](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/459), so this redesign draws the monitor as a flat field of the decoded signal.

### 2.4 Brand

`back-pocket-logo.png` at the repository root is the brand mark. Leave the file where it is. Do not redraw, recolor, crop, or replace it.

Use it in three places:

- The Instance hub header, at 28px.
- The desktop window icon and, when [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) builds the browser head, the page icon.
- The About block in the Platform hub, at 48px.

The accent was checked against that file on 2026-11-25. Sampling excluded the cream background and the near-black outline. Orange handle pixels (red above 170, green between 70 and 190, blue under 100, and red at least 40 above green) have a median of `#D98E37`. The most saturated quarter of those pixels averages `#ED882E`. The brightest handle highlights average `#F39135`. The pocket denim median is `#6591A9`.

`#F29A2E` sits in that highlight band and is the dark-theme accent. On `#141311` its contrast is 8.3:1, so it works as both a fill and as text. `#F29A2E` on the light paper `#F6F3EC` is 2.0:1, and the logo's own median `#D98E37` is 2.4:1 there. A 1px focus ring needs 3:1, and small text needs 4.5:1, so the light theme keeps the same hue (about 33°) and uses two values: `#AD732B` for fills, rings, and the running dot (3.6:1 on the paper, and `#1A1208` on that fill clears 4.5:1) and `#9B6017` for mango text (clears 4.5:1 on the paper). Both themes are specified in section 9.

### 2.5 Keycaps

`KeyCap` draws the Open Pocket and Closed Pocket glyphs from Josh's `pocket2-charset.rom` (embedded as `BadMango.Emulator.Devices.Resources.pocket2-charset.rom`). It draws those glyphs for the two modifier keys the guest still reads as pushbuttons. UI copy uses the pocket names.

The alternate character set in that ROM holds the glyphs:

| Glyph | Alternate-set code | Shape | Guest pushbutton |
| --- | --- | --- | --- |
| Closed Pocket | `$40` | Filled pocket: a flat welt, a nearly solid body, a pointed bottom, and one open scanline at the mouth | PB1, `$C062` |
| Open Pocket | `$41` | The same silhouette as an outline, with an open interior and a pointed bottom | PB0, `$C061` |

The rest of the alternate set (`$42`–`$5F`, and the mirrored range the character generator already uses) is MouseText: rules, arrows, a checker, a folder, and the other pictographs in that ROM. MouseText belongs on the monitor when the guest selects the alternate character set. Chrome does not use it.

`KeyCap` also renders word keys (`CTRL`, `SHIFT`, `RESET`, `F10`) in IBM Plex Mono. Those are type, not ROM glyphs.

The soft-switch behavior is unchanged from the [Keyboard Mapping Specification](../Keyboard%20Mapping%20Specification.md). Open Pocket held through reset is the cold-start chord the control plane already exposes. The keyboard spec's historical key names stay in that document; `bpui` shows the pocket names.

Mockups in this spec draw a geometric pocket (a flat top and a pointed bottom) as a stand-in. They do not trace ROM pixels.

### 2.6 Artwork

Chrome and mockups use the brand mark, the geometric keycap stand-in, hairlines, and type. They do not include Apple artwork, Apple logos, rainbow stripes, a beige computer body, a CRT frame, ROM bitmaps, or third-party images. Guest video inside `MonitorSurface` is the presentation stream from the host. Sample text in the mockups is original and is there to show phosphor color and type.

## 3. Information architecture

```
Shell
├── Machine hub          one instance, one or more monitor views
│   ├── Top chrome       power, run, pause, reset, speed, monitor, capture, debug, editor
│   ├── MonitorSurface   the hub
│   ├── Status strip     speed, FPS, drive activity, load, panel switches
│   └── Panel host
│       ├── Drives       DriveBay per reported drive
│       ├── Slots        cards, memory, CPU, firmware
│       ├── Input        keyboard, paddles, mouse
│       └── Status       CPU, clock, scheduler summary
├── Instance hub
│   ├── Instance list    state, model, connectivity, last-frame still
│   └── Instance detail  name, profile, launch, connectivity, state, remove
└── Platform hub
    ├── App              theme, paths, startup, logging
    ├── Packages         modules, clean-room firmware, device packages
    ├── Host services    printer, serial, network, folder, clipboard, gamepads, audio
    ├── Media library    shared images
    └── About            brand mark, version
```

The six-item sidebar (Machine Manager, Storage, Display, Debug, Editor, Settings) goes away with [#751](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/751). Debug windows stay in Debug.UI. The editor stays the TextEditor control, opened from the Machine hub, themed through the keys in section 12.

## 4. Navigation

The shell puts three text buttons at the leading edge of the top chrome: Machine, Instances, Platform. The active hub uses `ThemeAccentText` and a 2px underline. The others use `ThemeForegroundMuted`. This switcher is navigation. It is distinct from the segmented control, which changes a mode inside a hub.

Shortcuts, honored only while guest input is not captured:

| Shortcut | Hub |
| --- | --- |
| Ctrl+1 | Machine |
| Ctrl+2 | Instances |
| Ctrl+3 | Platform |

Opening an instance from the Instance hub selects it and shows the Machine hub. Removing the instance that is open returns the Machine hub to its empty state.

The Machine hub with nothing open is one line of muted text, "Open an instance to use the monitor," and a ghost button "Choose an instance" that navigates to the Instance hub.

The last hub and the last instance id are app-local settings. Panel layout persists per instance id: which panel is open, whether it is docked or popped out, and the docked width.

## 5. Machine hub

Built by [#747](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/747). The view is a `UserControl`.

### 5.1 Top chrome

A single 48px bar on `ThemeBackgroundRaised`, with a hairline under it. From leading to trailing:

1. Hub switcher.
2. Instance name, Inter 15px.
3. View switcher, a segmented control of the instance's monitor views, plus a trailing ghost `+` that adds a view.
4. Monitor picker, a segmented control: Mono, Green, Amber, NTSC. RGB outputs show the same control with every segment disabled and the value "RGB" in faint type. The picker sends a choice. Where that choice is decoded is [#738](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/738).
5. Open Pocket and Closed Pocket `KeyCap`s, reflecting PB0 and PB1.
6. Speed throttle, a segmented control: `½×`, `1×`, `2×`, `Max`. This sets the host throttle. It is separate from the measured speed on the status strip.
7. Running state. While the instance is running, a 6px mango dot and the word "Running" in `ThemeAccentText`. While paused, the word "Paused" in muted type and no dot. While stopped, the word "Off".
8. Ghost buttons: Pause (or Run, when paused), Reset, Stop. When the instance is stopped, Pause and Reset are disabled and the primary button is Power. Stop asks for confirmation only when a drive is dirty; the host reports that.
9. Capture. Idle label "Capture". While captured, the label is "Captured · F10" in `ThemeAccentText`. F10 is the default host key that releases capture. The input panel can change the key. The chip shows the current key.
10. Ghost buttons Debug and Editor. Debug attaches the debugger through the control plane's debugger extension and does not embed Debug.UI. Editor opens TextEditor in a desktop window (a slide-out on the browser head).

Reset while Open Pocket is down is the cold-start chord. The keycap shows the down state for as long as the host reports the button held.

### 5.2 The monitor

`MonitorSurface` ([#746](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/746)) fills the space between the top chrome and the status strip. It is inset 16px on `ThemeBackground`. The field itself is `ThemeMonitor` (`#0C0B09`) in both themes, because the field is the signal, not chrome. A 1px `ThemeHairline` outlines the field. Integer or aspect-correct scaling, chosen in App configuration, letterboxes inside the field. Radius 0. No bezel, no scanlines, no drop shadow.

One instance can have several views. Each view has its own monitor choice and its own capture state, and each subscribes to the same presentation stream. Two surfaces on one composite source may show Green and Amber at the same time. An RGB source passes the finished frame through and locks the picker.

A drag onto the monitor inserts into the drive the host marks as the boot drive. If the host marks none, the drop is refused and the status strip says "Choose a drive."

The light theme does not lighten the monitor field.

### 5.3 Status strip

A 36px bar on `ThemeBackgroundRaised` with a hairline above it. IBM Plex Mono at 12px for the figures, Inter 11px for the captions.

| Item | Source |
| --- | --- |
| Measured speed, `100%` | Control plane, distinct from the throttle |
| Frame rate, `59.9 FPS` | Presentation stream |
| One activity LED and a slot label per drive | Control plane topology and activity |
| Load | `FileLoadProgress` while a load is in progress. At rest the strip omits it |

The trailing end of the strip holds four text buttons: Drives, Slots, Input, Status. The open panel's button uses `ThemeAccentText`.

Clicking a drive LED opens Drives and scrolls that bay into view.

### 5.4 Slide-out and pop-out

The panel host has one slot on the trailing edge of the Machine hub.

**Slide-out** is the default for Drives, Slots, Input, and Status. The panel is 320px wide (the user may drag it between 280 and 480; the width persists). It pushes the monitor; it does not cover it. The panel background is `ThemeBackgroundRaised`. A hairline separates it from the monitor. Only one panel is in the slot. Choosing another panel from the status strip replaces the slot's content. The panel header is 48px: the panel name in Inter 15px, and a ghost button "Pop out".

**Pop-out** is the same `UserControl` hosted in its own window. The window title is the panel name and the instance name. Its header replaces "Pop out" with "Dock". Closing the window docks the panel back into the slot. It does not discard the panel. A popped panel leaves the slot empty and the monitor reclaims the width. The status-strip button stays in the accent color.

**View pop-out** uses the same host rule for a monitor view. The view switcher grows a "Pop out" action when that view is selected. The popped view is a `MonitorSurface` with its own monitor choice. Dock returns it to the switcher.

Motion is 160ms, ease-out, with no overshoot. When the OS asks for reduced motion, the panel snaps.

On the browser head there is no window to pop out into. "Pop out" is omitted. Slide-out and the view switcher remain. Section 14 records this.

### 5.5 Slots panel

[#749](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/749). A slot strip for S1–S7 and aux, each row a slot label in IBM Plex Mono, the card name, and that card's settings. Below the strip: memory, CPU, and firmware source.

Firmware choices the host may offer are clean-room and user-supplied upload. The panel shows the source the host reports.

While the instance is running the panel is read-only, and a faint line reads "Stop the instance to change slots." Edits that need a restart carry the faint line "Takes effect on the next power-on." Edits go through the control plane.

### 5.6 Input panel

[#750](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/750).

- Keyboard. The active mapping from the Keyboard Mapping Specification, as a list of host key to guest key. Open Pocket and Closed Pocket are `KeyCap`s bound to the host keys the map assigns (the default map uses Left Alt and Right Alt). A ghost action "Paste clipboard" injects text through the input plane.
- Joystick and paddles. The host gamepad, or mouse-as-joystick, plus deadzone and calibration. The device list comes from the host.
- Mouse. Capture mode and the release key. The release key is the same key the capture chip displays.

The panel does not edit map files, and it does not implement the AppleMouse card ([#451](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/451)).

### 5.7 Status panel

Read-only. CPU, clock, and a scheduler summary from the control plane's machine info. Debugger windows stay in Debug.UI. This panel is the summary a person can read without attaching the debugger.

## 6. Drive bays

Built by [#748](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/748). One `DriveBay` per drive in the topology the host reports for that instance. The UI does not hard-code a model. The host's topology is what keeps these rules intact:

- A Liron profile has two UniDisk 3.5 drives and no 5.25-inch drive.
- On the IIgs, slot 5 is the SmartPort chain, slot 6 is the Disk Port, and 5.25-inch drives sit at the end of the chain.
- On the IIc, the external 5.25-inch drive is S6,D2, and the 3.5 ROM puts UniDisk 3.5 drives ahead of it.

Tests in [#748](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/748) assert those three profiles. The mockups use `S6 D1`, `S6 D2`, and `S5 D1` as sample labels, which is what a host might report, not a layout the view assumes.

### 6.1 Anatomy

Each bay is a row in the Drives panel, separated by a hairline. The row holds:

- A 6px activity LED.
- The slot label in IBM Plex Mono (`S6 D1`).
- The image name, or "Empty".
- A mono meta line: track (`T $12`) or block (`B 0042`), and `WP` when the image is write-protected.
- Actions, as ghost buttons or accent text, depending on state.

The LED is the strong hairline color when idle, mango while the host reports activity, and off (the bay uses danger text instead) in the error state. Activity is a property of the inserted and write-protected states. It is not a sixth state.

### 6.2 States

| State | LED | Name line | Meta | Actions |
| --- | --- | --- | --- | --- |
| Empty | Idle | Empty | The slot label only | Insert, New image |
| Inserted | Idle, mango while active | The image name | Track or block | Eject, Write-protect |
| Write-protected | Same as inserted | The image name | Track or block, plus `WP` | Eject. Write-protect reads as on. The host refuses writes |
| Busy | Mango, steady | The image name | "In use" | Every action disabled until the host allows a change |
| Error | Idle | The name the user tried, or "Empty" if nothing is mounted | The host's message, in `ThemeDanger` | Eject, Retry |

Insert opens a chooser in the panel. The chooser lists library entries whose format the drive reports it can take, then a "Choose file…" row that uses `StorageProvider`, which works on the desktop and in the browser. A drag onto a bay inserts there.

New image offers only the formats and sizes that drive reports. The new file is added to the media library and inserted into the bay.

Eject unmounts through the control plane. A dirty image asks for confirmation.

Write-protect toggles the host flag. The bay shows `WP` for as long as the host reports the flag.

Busy covers a motor-on drive, an in-flight mount, and any other condition the host flags as refusing mutation. The row stays visible. The actions dim to 40% opacity and do not take clicks.

Error is a failed mount or an image the drive rejected. The message is the host's string. Retry repeats the last mount. Eject clears the bay.

Only user-supplied files and blank images appear. The bay never offers a bundled disk or a ROM.

### 6.3 Library versus bay

The bay does not own the catalog. Insert reads the Platform hub's media library. A file chosen through `StorageProvider` is added to the library and then mounted, so the next instance can see it. Removing a library entry does not eject a drive that currently has it mounted; the bay keeps the path until Eject.

## 7. Instance hub

Built by [#751](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/751). The view is a `UserControl`. It replaces `MachineManagerView`, `MachineManagerViewModel`, the sidebar, and `PlaceholderView`.

### 7.1 Layout

Top chrome, 48px, hub switcher leading, the brand mark at 28px, the word "Instances" in Inter 20px, and one primary button, "Add instance", trailing.

Below the chrome, a hairline splits a 480px list from the detail. List rows are 72px, separated by hairlines. The selected row carries a 2px mango bar on its leading edge. The row background stays `ThemeBackground`.

Each row shows:

- A 96×54 still from the latest presented frame, on `ThemeMonitor`, refreshed about once a second. The list does not open a second live surface per row.
- The instance name, Inter 15px.
- The model, the connectivity kind, and the run state. "Running" uses `ThemeAccentText` and the 6px dot. "Paused", "Off", and a remote "Reconnecting" use muted type.

### 7.2 Detail

The detail pane is the selected instance, or a create form after "Add instance".

- Name.
- Profile, chosen from the profiles the control plane lists.
- Launch options. "Start paused" is the one option this spec requires. Further options wait until the control plane exposes them.
- Connectivity, a segmented control: In-process ([#735](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/735)), Local host ([#736](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/736)), Remote ([#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737)). Under it, a mono health line: "Loopback · healthy", "Local host · disconnected", "remote.example · reconnecting", and the same shape for a refusal. The remote path does not assume a trusted localhost; auth and the firmware rule are [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737).
- State. Ghost buttons Pause (or Run), Reset, Stop, and Open. Open shows the Machine hub for this instance. While the instance is stopped, the primary button in the pane is Power and "Add instance" in the chrome stays the hub's primary. A surface shows at most one filled primary; while a create form is open, "Create" is that button and "Add instance" becomes a ghost.
- Remove. A danger-text button "Remove instance". Activating it replaces the button with the line "Remove Workshop? Disk images stay in the library." and two actions, danger "Remove" and ghost "Cancel".

Hardware detail (slots, drives, input) stays on the Machine hub. Package installs stay on the Platform hub.

Every action goes through the host API. The list is the host's instance list. The Phase 1 sample profiles in `MachineManagerViewModel` are deleted with that view model.

## 8. Platform hub

Built by [#752](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/752). The view is a `UserControl`. It replaces the Settings window and its pages.

The top chrome matches the other hubs. The content is one column, inset 32px, sections stacked. A section title is Inter 20px. A hairline and 24px of space separate sections. There is no card around a section.

An option with nothing behind it is omitted. Anonymous usage data and update checks are omitted until [#657](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/657) exists. The current General settings flags for telemetry and update checks are not migrated.

### 8.1 App

Theme (Dark, Light, System) as a segmented control. Library path, startup ("Open the last instance"), and logging level. Display scaling (integer or aspect-correct) lives here because it is a property of the shell, while the monitor choice lives on each Machine hub view. Scanlines and CRT effects are [#459](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/459) and have no control here.

### 8.2 Packages

List, install, update, and remove modules, clean-room firmware, and device packages, following [#473](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/473), [#474](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/474), [#655](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/655), [#656](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/656), and [#658](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/658). The screen lists what is installed and a ghost "Install package". A faint line reads "Clean-room or user-supplied." The screen never offers Apple or third-party ROMs. The package format itself stays in the [#473](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/473) epic.

### 8.3 Host services

One row per service, a hairline between rows, the state as a word ("Connected" or "On" in foreground, "Off" in faint), and a ghost Configure. A connected service does not use mango. Mango on this hub is the primary action and focus.

| Service | Follows | Plane |
| --- | --- | --- |
| Printer and serial | [#449](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/449) | Host service. TCP bridge, print to PDF or PNG |
| Network | [#447](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/447) | Host service |
| Host folder as a ProDOS volume | [#455](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/455) | Host service |
| Clipboard paste | Input plane | Enables the paste action on the input panel |
| Host gamepads | [#451](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/451) | Input plane, device list for the input panel |
| Audio out | [#691](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/691) | Presentation plane. The toggle selects the sink for the PCM stream. The hub does not call `IAudioOutput.Play` |

Printer, serial, network, and the folder bridge sit outside the three [#734](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/734) planes. They are enabled through the host's service-connection surface and the module permission model, which [#752](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/752) adds beside those planes. Toggles do not take a reference to a device type.

### 8.4 Media library

The section title is "Media library". The line under it reads "Shared by every instance. Drive bays insert from here."

Rows are display name, size, and format, in IBM Plex Mono for the size. Actions are Add files (`StorageProvider` and drag-and-drop), New image (format and size from what the host can create, with no drive attached), and Remove. Remove is refused with the host's message when a bay still has the image mounted; the faint line names the instance.

### 8.5 About

The brand mark at 48px, "Back Pocket" at 20px, and the version, build, and runtime in IBM Plex Mono. License and source links stay. The block shows the same mark the window icon uses.

## 9. Tokens

[#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744) ships these from `BadMango.Emulator.Theme`, a resources-only assembly whose only dependency is Avalonia. `bpui`, Debug.UI, and the browser head reference it. TextEditor does not.

FluentTheme stays the behavior base. The token dictionaries override Fluent's resource keys, including `SystemAccentColor` (from `ThemeAccent`) and the control corner radius (from `ThemeRadiusControl`). Light is a swap of the color dictionary. Views use the keys. Views do not contain hex literals. [#744](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/744) includes a check that fails the build when `bpui` view XAML contains one.

### 9.1 Color

`ThemeMonitor` is the same value in both dictionaries.

| Key | Dark | Light | Use |
| --- | --- | --- | --- |
| `ThemeBackground` | `#141311` | `#F6F3EC` | Window and hub ground |
| `ThemeBackgroundRaised` | `#1B1A17` | `#FFFCF7` | Chrome, panels, popped windows |
| `ThemeBackgroundSunken` | `#100F0D` | `#EBE6DC` | Fields, empty thumbnails, the inset around the monitor |
| `ThemeHover` | `#24221E` | `#EFEAE2` | Ghost and row hover. A step off the raised surface |
| `ThemeMonitor` | `#0C0B09` | `#0C0B09` | Monitor field. Guest video draws on top |
| `ThemeForeground` | `#E8E2D6` | `#1C1915` | Primary text. 14:1 on dark, 15:1 on light |
| `ThemeForegroundMuted` | `#9C958A` | `#5C564C` | Secondary text. At least 6:1 |
| `ThemeForegroundFaint` | `#8E8278` | `#70685C` | Captions. At least 4.5:1, so 11px labels stay readable |
| `ThemeHairline` | `#34302A` | `#E4DCD0` | 1px dividers |
| `ThemeHairlineStrong` | `#4A453C` | `#C9C0B2` | Control borders, keycap edges, idle LED |
| `ThemeAccent` | `#F29A2E` | `#AD732B` | Focus ring, primary fill, running dot, activity LED, selected toggle thumb |
| `ThemeAccentText` | `#F29A2E` | `#9B6017` | Mango text and the active hub underline |
| `ThemeOnAccent` | `#1A1208` | `#1A1208` | Text on an accent fill. 8.3:1 on the dark accent, 4.6:1 on the light accent |
| `ThemeSelection` | `#453117` | `#E2CFB6` | Solid selection fill, pre-blended so views do not use alpha |
| `ThemeDanger` | `#E15A4A` | `#B83E32` | Error text and the destructive label. At least 4.5:1 |

Phosphor colors are monitor-content values inside `MonitorSurface`, not chrome tokens and not Fluent overrides:

| Monitor choice | Field text |
| --- | --- |
| Mono | `#E6E2D8` |
| Green | `#B7E39A` |
| Amber | `#F0C36A` |
| NTSC | The decoded frame, untinted |
| RGB | The finished frame, picker locked |

`ThemeDanger` is a functional color for errors and the destructive confirm. It is not a second accent: it does not fill a toolbar, a selection, or a running state. A destructive button is ghost-shaped with danger text, not a filled red button.

### 9.2 Type

`ThemeFontUi` is Inter. `ThemeFontMono` is IBM Plex Mono.

| Key | Size | Line height | Use |
| --- | --- | --- | --- |
| `ThemeFontSizeMicro` | 11 | 14 | Captions, keycap word-keys, status captions |
| `ThemeFontSizeCaption` | 12 | 16 | Secondary UI, mono figures |
| `ThemeFontSizeBody` | 13 | 18 | Default UI, hub switcher |
| `ThemeFontSizeBodyLarge` | 15 | 20 | Instance name in the machine chrome, panel titles |
| `ThemeFontSizeTitle` | 20 | 28 | Hub titles, section titles |
| `ThemeFontSizeDisplay` | 28 | 36 | Reserved. Current hubs do not use it |

Mono figures use `ThemeFontSizeCaption` unless a panel is showing a single address, which may use `ThemeFontSizeBody`.

### 9.3 Spacing

A 4pt scale. Keys: `ThemeSpace1` 4, `ThemeSpace2` 8, `ThemeSpace3` 12, `ThemeSpace4` 16, `ThemeSpace5` 24, `ThemeSpace6` 32.

Chrome padding is 16px (`ThemeSpace4`). The gap between a caption and its value is 4px. Section padding in the Platform hub is 32px. Rows inside a panel use 12px of vertical padding.

### 9.4 Radius

| Key | Value | Use |
| --- | --- | --- |
| `ThemeRadiusControl` | 2 | Buttons, fields, segmented controls, thumbnails. Overrides Fluent `ControlCornerRadius` |
| `ThemeRadiusPanel` | 4 | Popped windows |
| `ThemeRadiusKeycap` | 3 | `KeyCap` |
| `ThemeRadiusMonitor` | 0 | `MonitorSurface` |

### 9.5 Focus, density, motion

The focus ring is a 1px `ThemeAccent` stroke, 2px outside the control. Every themed control in [#745](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/745) shows it. Chrome controls are at least 28px tall. Slide-out motion is 160ms ease-out, and it snaps when the OS requests reduced motion.

### 9.6 How a surface is built

A surface is ground, hairlines, type, and at most one accent fill. The practices that keep it that way:

- Separators are 1px hairlines.
- The selected list row is a 2px leading bar in `ThemeAccent`.
- The selected segment and the active hub are accent text plus, for the hub, a 2px underline.
- The on state of a toggle is a mango thumb on a strong-hairline track.
- Buttons are primary (accent fill, on-accent text), ghost (hairline, foreground text), or danger (hairline, danger text).
- Icons that ship carry a text label or a tooltip. The hub switcher, the panel switches, and the bay actions are text.

Filled cards, drop shadows, gradients, a second accent, a rainbow, a beige body, and a CRT frame are outside this language.

## 10. Controls

| Control | Issue | Role |
| --- | --- | --- |
| `MonitorSurface` | [#746](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/746) | Presents one view's stream, captures input, accepts a drop on the boot drive |
| `DriveBay` | [#748](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/748) | One reported drive: LED, name, position, insert, eject, create |
| `StatusStrip` | [#747](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/747) | Measured speed, FPS, drive LEDs, load, panel switches |
| Panel host | [#747](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/747) | Slide-out slot and desktop pop-out window for a panel or a monitor view |
| Segmented control | [#745](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/745) | Monitor, throttle, connectivity, theme, view switcher |
| `KeyCap` | [#750](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/750) | Open Pocket, Closed Pocket, and mono word keys |

[#745](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/745) also themes Button (primary, ghost, danger), ToggleButton, ListBoxItem, TextBox, ComboBox, ScrollBar, TabStrip, and ToolTip. Those themes use the tokens above. Domain controls (`MonitorSurface`, `DriveBay`) are not part of [#745](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/745).

The segmented control is an inline group, 28px tall, radius 2, with a hairline around the group and between segments. The selected segment is `ThemeAccentText` with no fill. An unselected segment is `ThemeForegroundMuted`. A disabled segment is `ThemeForegroundFaint`. Arrow keys move the selection.

`KeyCap` is 28×28, radius 3, fill `ThemeBackgroundRaised`, border `ThemeHairlineStrong`. The up state draws the glyph in `ThemeForegroundMuted`. The down state draws it in `ThemeForeground` and the border in `ThemeAccent`. Disabled draws `ThemeForegroundFaint`. The glyph is the ROM bitmap for `$40` or `$41`, scaled to the cap with integer scaling and centered. Word keys set the mono string instead of a glyph.

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
| Control | Power, run, pause, reset, stop, state, clock, throttle, instances, profiles, disks (`MountDisk`, `EjectDisk`, `ListDisks`), attach and detach the debugger, machine info |
| Presentation | Subscribe to video frames or a raw composite signal, send a monitor choice, read frame timing, subscribe to the PCM audio stream. The client does not call `IAudioOutput.Play`. Decode location follows [#738](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/738) |
| Input | Keyboard down and up with modifiers, joystick, paddles, buttons, mouse move and buttons, capture and release |

The debugger extension of the control plane (breakpoints, watches, memory, disassembly) belongs to Debug.UI. `bpui` uses attach and detach, plus the machine-info summary.

### 13.1 Target view models

| View model | Hub | Contract |
| --- | --- | --- |
| `ShellViewModel` | Shell | Control, read-only: instance ids, names, and run state for the switcher. No presentation subscription |
| `MachineHubViewModel` | Machine | Control: run, stop, pause, reset, state, clock, throttle, dirty-drive flag, attach and detach debugger. Addresses one machine id |
| `MonitorViewViewModel` | Machine | Presentation: frames or raw composite signal, monitor choice, frame timing. Input: keys, mouse, joystick and paddles while captured, capture and release. One view |
| `DriveBaysViewModel` | Machine | Control: `ListDisks` topology, activity, boot-drive id |
| `DriveBayViewModel` | Machine | Control: `MountDisk`, `EjectDisk`, write-protect, create-blank capability, busy and error. The chooser reads `MediaLibraryViewModel`, then mounts |
| `SlotsPanelViewModel` | Machine | Control: slot map, memory, CPU, firmware source. Writes only while stopped |
| `InputPanelViewModel` | Machine | Input: active map (read), pocket-key state, clipboard paste as key injection, gamepad and paddle calibration, mouse capture mode, release key |
| `MachineStatusPanelViewModel` | Machine | Control, read-only: CPU, clock, scheduler summary from machine info |
| `PanelHostViewModel` | Machine | None. Dock, pop-out, width, and which panel is showing are shell UI state |
| `InstanceHubViewModel` | Instance | Control: list, add from profile, remove |
| `InstanceRowViewModel` | Instance | Control: state, model, connectivity, health. Presentation: a still of the latest frame, about once a second |
| `InstanceConfigViewModel` | Instance | Control: name, profile, launch options, connectivity kind |
| `ConnectivityViewModel` | Instance | Control: transport kind and health for in-process, local host, and remote. The remote transport is [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737); the view model still calls the [#734](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/734) control contracts |
| `PlatformHubViewModel` | Platform | None. Section selection is shell UI state |
| `AppConfigViewModel` | Platform | None of the three planes. Theme, library path, startup, logging, and display scaling are app-local settings |
| `PackageManagerViewModel` | Platform | None of the three planes. Module permission model ([#473](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/473)) |
| `HostServicesViewModel` | Platform | Input for clipboard paste and host gamepads. Presentation for the audio-out sink. Printer, serial, network, and the host folder are the service-connection surface beside the three planes |
| `MediaLibraryViewModel` | Platform | None of the three planes. Catalog only. A bay turns an entry into a control-plane `MountDisk` |
| `AboutViewModel` | Platform | None. Brand mark, version, build |

### 13.2 Current view models

These are the view models in `src/BadMango.Emulator.UI/ViewModels` today, and where each one goes. None of them call the host API yet. The stub data in `MachineManagerViewModel` is not a contract and is not migrated.

| Current | Disposition | Takes its place |
| --- | --- | --- |
| `MainWindowViewModel` | Replaced | `ShellViewModel` |
| `NavigationItemViewModel` | Deleted with the six-button sidebar | Hub switcher inside `ShellViewModel` |
| `PlaceholderViewModel` | Deleted | The three hubs |
| `MachineManagerViewModel` | Deleted ([#751](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/751)) | `InstanceHubViewModel` |
| `MachineInstanceViewModel` | Replaced | `InstanceRowViewModel` |
| `MachineProfileViewModel` | Replaced | Profile data on `InstanceConfigViewModel`, from the control plane |
| `PopOutWindowViewModel` | Replaced | `PanelHostViewModel` for panels and monitor views |
| `SettingsWindowViewModel` | Deleted | `PlatformHubViewModel` |
| `SettingsPageViewModelBase` | Deleted | Section view models below |
| `GeneralSettingsViewModel` | Replaced. Telemetry and update checks are dropped | `AppConfigViewModel` |
| `LibrarySettingsViewModel` | Split | Paths on `AppConfigViewModel`, images on `MediaLibraryViewModel` |
| `DisplaySettingsViewModel` | Split. Scanlines and CRT palette controls are not migrated | Monitor choice on `MonitorViewViewModel`; scaling on `AppConfigViewModel` |
| `InputSettingsViewModel` | Replaced. Becomes per instance | `InputPanelViewModel` |
| `DebugSettingsViewModel` | Split | Attach on `MachineHubViewModel`; log level on `AppConfigViewModel` |
| `EditorSettingsViewModel` | Replaced by resource keys | Section 12. Host mapping is [#753](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/753) |
| `AboutViewModel` | Kept, moved under the Platform hub | `AboutViewModel` |

## 14. What the browser head reuses

[#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) is an Avalonia browser client on the same host API. It reuses views and view models. It does not get a second visual language.

Reused as `UserControl`s, with the view models in section 13.1:

- The shell and the hub switcher.
- The Machine hub, including `MonitorSurface`, `StatusStrip`, and the Drives, Slots, Input, and Status panels.
- `DriveBay` and the media-library chooser inside it.
- The Instance hub, including the connectivity segment. Remote is the browser's ordinary case; in-process remains available when the page hosts a loopback.
- The Platform hub, including App, Packages, Host services, Media library, and About.
- TextEditor, when the shell opens it, themed through section 12.

Desktop only:

- The pop-out `Window`. The browser omits "Pop out" and keeps the slide-out.
- Debug.UI windows. The browser slice attaches, presents frames, plays the PCM stream, and sends keyboard, gameport, and mouse input. Debugger windows stay on the desktop ([#753](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/753)).

Both hosts open files through `StorageProvider` and drag-and-drop. Media is user-supplied. The firmware rule from [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) stands: the host does not serve Apple ROMs, and a remote machine boots clean-room firmware or firmware the user uploaded.

Input latency on the network path is the one [#737](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/737) documents. The local path stays on its own transport. The view models do not branch on that choice; the transport behind the contracts does.

## 15. Mockups

Static SVG, drawn for this spec. They reference `back-pocket-logo.png` by relative path and do not copy it. The pocket marks on the keycaps are geometric stand-ins, described in section 2.5. Guest text on the monitor is original sample text so the phosphor and the mono face are visible.

| File | What it shows |
| --- | --- |
| [`mockups/machine-hub.svg`](mockups/machine-hub.svg) | Machine hub, dark, monitor-centric, chrome top and bottom, pocket keycaps |
| [`mockups/machine-hub-light.svg`](mockups/machine-hub-light.svg) | The same hub in the light theme. The monitor field stays `#0C0B09` |
| [`mockups/machine-hub-slide-out.svg`](mockups/machine-hub-slide-out.svg) | Drives panel slid out of the trailing edge, monitor yielded |
| [`mockups/machine-hub-pop-out.svg`](mockups/machine-hub-pop-out.svg) | The same Drives panel popped into its own window, monitor restored to full width |
| [`mockups/drive-bay-states.svg`](mockups/drive-bay-states.svg) | Empty, inserted, write-protected, busy, and error |
| [`mockups/instance-hub.svg`](mockups/instance-hub.svg) | Instance list, detail, connectivity, brand mark |
| [`mockups/platform-hub.svg`](mockups/platform-hub.svg) | App, packages, host services, media library, About |

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
| [#746](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/746) | `MonitorSurface`, section 5.2 |
| [#747](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/747) | Machine hub chrome, status strip, panel host |
| [#748](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/748) | `DriveBay` and section 6 |
| [#749](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/749) | Slots panel |
| [#750](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/750) | Input panel and `KeyCap` |
| [#751](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/751) | Instance hub, deletion of the Machine Manager stub and placeholders |
| [#752](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/752) | Platform hub, including the media library |
| [#753](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/753) | Debug.UI adopts the theme and maps section 12 |
| [#754](https://github.com/Bad-Mango-Solutions/back-pocket-emulator/issues/754) | TextEditor keys in section 12, with neutral defaults |
