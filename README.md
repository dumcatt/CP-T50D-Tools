# CP-T50D Tools

A Windows app for browsing SOUND VOLTEX generator cards and printing them on a **Mitsubishi CP-T50D-KM** or exporting them to a PNG file.
- Reads the game's card archives (`.ifs`) directly and builds each card with the game's own frame, plaque, text layout and per-card brightness.
- Prints through Mitsubishi's printer library (`CPUSBXPKM.dll`), using the same image format, rotation and settings as the game.
- Glossy or KMAT 1–6 overcoat finishes, print density, pre-heat and a vertical print offset.
- Print several cards in one go as a queue.
- Live printer status (cards left, head temperature, lifetime prints) and a paper/ink change standby mode.

> You need your own copy of the game data and the printer's software from the cabinet. None of it is included here.

---

## What you need

| | |
|---|---|
| **Printer** | Mitsubishi CP-T50D-KM connected by USB, with its USB driver installed (see below) |
| **Printer library** | `CPUSBXPKM.dll` and `MUsbPuIf.dll` (64-bit), from the cabinet's `System32` folder |
| **Game data** | A SOUND VOLTEX `contents` folder containing `data\graphics\chara_card`, `data\others\genesis_card.xml` and `ext\cpusbxpkm_data` |

The `ext\cpusbxpkm_data` folder holds the printer's colour table, thermal compensation tables and overcoat patterns (`km1_lut.lut`, `km1_n_*.csv`, `kmat_*.raw`). **Printing does not work without it.**

---

## Setup

### 1. Install the printer's USB driver

Without the driver, the printer appears in Device Manager as **MITSUBISHI USB Printer** under *Universal Serial Bus controllers*.

1. Disable Driver Signature enforcement
2. Open **Device Manager**, right-click **MITSUBISHI USB Printer** and choose **Update driver → Browse my computer → Let me pick → Have Disk**.
3. Select `mpusbudf.inf` from the cabinet's driver files and confirm the warning about the unsigned driver.

Afterwards the printer should show up as **MITSUBISHI UMDF USB Port(CP-T50D)**.

### 2. Get the app

Download or build `CPT50DTools.exe` (see [Building from source](#building-from-source)). It is a single self-contained file with no install step, and does not need .NET installed.

### 3. First run

1. Start `CPT50DTools.exe`.
2. **File → Load game data folder…** (`Ctrl+Shift+O`): select the game's `contents` folder (the game root or `contents\data` also work).
   - The cards appear in the browser, grouped by pack.
   - The printer data folder (`contents\ext\cpusbxpkm_data`) is found automatically. The **Log** tab shows which folder was used.
   - The card font is taken from the game's `data\graphics\font` folder when it is there.
3. **Options → Load Printer SDK folder…**: select the folder that contains `CPUSBXPKM.dll` and `MUsbPuIf.dll`.
   - The dot in the bottom-right corner turns **green** with "CP-T50D ready (N cards left)" once the printer is found.
   - **Options → Printer status** shows model, firmware, media, cards left, head temperature and lifetime prints.

Both folders are remembered, so next time the app loads the printer library and connects on its own. Load the game folder again to show the cards.

### 4. Calibrate the print position

Printers cut the card slightly differently, so check yours once:

1. **Options → Print test card**. It has rulers on every edge with a line every 5 px (yellow every 10 px).
2. Count how many ruler lines are left at the **top** and **bottom** of the printed card.
3. If the top shows fewer lines than the bottom, raise **Print options → Print offset** by half the difference; if the bottom shows fewer, lower it. One pixel is about 0.08 mm. The default is 14.

The offset only changes the printed image, not the on-screen preview.

---

## Using the app

### Browsing cards

- **Search box:** filters by card or pack name, serial number (`SV05-N003-005`, `n003-005`, …), illustrator or title.
- **Thumbnail size** slider, and **Expand all / Collapse all** for the pack groups.
- **File → Load card file…** (`Ctrl+O`) opens single `.ifs` archives or images (PNG, JPG, BMP, GIF, TIFF). You can also drag files onto the card list.

### Selecting several cards

| Action | Result |
|---|---|
| Click | Select one card |
| `Ctrl`+click, or the checkbox in a tile's top-left corner | Add or remove a card |
| `Shift`+click | Select a range (`Ctrl`+`Shift`+click adds a range) |
| `Ctrl+A` | Select every card currently shown |
| `Esc` | Back to one card |
| Arrow keys / `Shift`+arrows | Move / extend the selection |

With several cards selected, **◀ 1/N ▶** under the preview steps through them.

### Preview

- **Mouse wheel** zooms around the cursor; **left-drag** pans.
- **⟲ Reset view** (bottom-right of the preview) or a **double-click** fits the card again.
- **Show bleed area** shows the whole printed area: the shaded part is cut off and the dashed line is the cut line.

### Card text

**Card text** edits the illustrator, title and serial printed on the selected card. Edits are remembered per card; **Reset to default** restores the text from `genesis_card.xml`.

### Print options

| Option | |
|---|---|
| **Overcoat** | Glossy, or one of the six KMAT patterns. A pattern overrides Density. |
| **Density** | -7 to +30. 0 matches the game. |
| **Print offset** | Moves the card down (+) or up (-) on the print. See [Calibrate the print position](#4-calibrate-the-print-position). |
| **Pre-heat** | Warms the print head before printing; useful for the first card from cold. |
| **Draw card frame / Print card text / Print copyright line** | Turn the game's overlays on or off. |

**Reset to default** restores the arcade settings. It does not change the print offset.

### Printing

1. Select one or more cards and click **Print Card** / **Print N Cards**.
2. Check the confirmation window. For a queue, **◀ ▶** (or the ← → keys) shows each card. Then click **Continue** / **Print N Cards**.
3. Cards print one after another. The status bar shows progress, and **Cancel Queue** stops after the current card.

If the printer runs out of cards or ribbon, printing stops and tells you how many cards were printed.

### Changing paper or ink

**Options → Printer status → Paper/Ink change** puts the app in standby: it stops talking to the printer so you can open the door and swap the card stock or ribbon. Click **Resume printer** afterwards to reconnect and read the new card count.

**Options → Export selected PNG…** (`Ctrl+E`) saves the selected card as shown in the preview (714×1074, bleed included).

---

## Troubleshooting

| Problem | Fix |
|---|---|
| Printer dot stays red / "Printer offline" | Check the USB cable and power, and that Device Manager shows **MITSUBISHI UMDF USB Port(CP-T50D)**. Check **Options → Printer status**, or click **Refresh** there. |
| Print fails with "not initialized (0x001)" or "No printer data folder" | The `ext\cpusbxpkm_data` folder wasn't found. Load the game folder with **File → Load game data folder…** and check the **Log** tab. |
| "Paper empty / ribbon exhausted (0x3F6)" | Use **Paper/Ink change**, refill, then **Resume printer**. |
| Card is cut too close to one edge | Adjust **Print offset** using the test card. |
| "Could not load the printer SDK" | Make sure both DLLs are the 64-bit versions and are in the same folder. To switch to a different SDK folder, restart the app. |
| Thumbnails or card text look wrong | Load the game folder (not just loose files), so `genesis_card.xml`, the frames and the font are found. |

The **Log** tab records everything the app does: folders, printer status changes and every print job with its settings.

Settings are stored in `%AppData%\CPT50DTools\settings.json`. Delete that file to start fresh.

---

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on 64-bit Windows.

Run from source (optionally passing a game folder to open on start):

```bash
dotnet run -- "C:\path\to\contents"
```

Build the single-file release into `publish\CPT50DTools.exe`:

```bash
dotnet publish -p:PublishProfile=win-x64
```

### Project layout

| Folder | Contents |
|---|---|
| `Cards/` | Card catalog (`genesis_card.xml`), compositing (frames, text, brightness), thumbnails, fonts, overcoat previews |
| `Controls/` | Card browser grid, preview (zoom / pan), collapsible sections |
| `Ifs/` | Reader for the game's `.ifs` archives, kbin XML and LZ77 compression |
| `Printer/` | `CPUSBXPKM.dll` interop, printer service (status polling, print jobs, standby), print image builder |
| `Resources/Frames/` | Fallback frame textures, used when the game's `card_data.ifs` is not available |

[printer_feature_handover_notes.md](printer_feature_handover_notes.md) documents the printer library: data structures, settings and status codes.
