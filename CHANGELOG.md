# Changes in this fork

## 0.7.1

- Add direct 4K/6K/8K lane allocation alongside the original single-lane view.
- Add inward-roll and alternating-hand modes, primary-hand/KPS controls, playback-speed
  density adjustment, real multitaps, and optional nearby-note visual alignment.
- Preserve hold occupancy and report lane shortages explicitly, without dropping requirements.
- Remove extra midspin and ordinary hold-release presses; respect automatic hold endpoints
  and the game's Hit Once option.
- Read the game's input clock directly, avoiding calibration drift from early/late player hits.
- Rebuild after editor timing changes even when the game reuses the same floor list.
- Add selectable Simplified Chinese / English menus and persistent numeric controls.
- Expand scroll speed to 10–10000 px/s, note thickness to 2–80 px, and hit-line thickness
  to 1–20 px. Position the line using 0–4320 px from the bottom or 0–100% from the top.
- Preserve older Settings.xml values and validate invalid values without corrupting layout.
- Add four game-independent test suites and a reproducible local build/package script.

Based on upstream 0.6.0 (`1da66fb83b341decd080ca3aef8aebe96cd521d8`).
