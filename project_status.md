# CoQ Auto-Map / Atlas

Status: somehow, no longer a prototype.

Atlas is an in-game automap mod for Caves of Qud. It captures explored zones as image tiles, saves them to the player's active save/cache directory, and stitches them together into a continuous layer-aware map. It also includes a world map overlay so players can tell where they are on the surface while exploring underground.

## Release candidate status

The core feature set is implemented and ready for normal-game playtesting.

## Implemented

- Captures explored zones as `.png` image tiles.
- Saves automap images under the player's active save/cache directory in `Automap\tiles`. Steam cloud compatable.
- Captures the current zone when Atlas opens.
- Captures zones automatically when leaving a zone.
- Does not reveal unexplored cells.
- Renders unexplored areas as black.
- Renders explored-but-not-currently-visible cells readably, without vanilla remembered-map darkening.
- Stitches captured zone images together by world, layer, parasang, and local zone position.
- Supports surface, subterranean, and sky layers.
- Provides a stand-alone Qud-styled Atlas UI.
- Shows the current displayed layer in the header.
- Supports keyboard pan, zoom, layer switching, and reset.
- Supports mouse drag-pan.
- Supports mouse wheel zoom.
- Mouse wheel zoom is centered on the cursor.
- Includes a world map overlay showing the current parasang while underground.
- Blocks normal game input while Atlas is open.
- Uses `Ctrl+M` to open Atlas.
- Uses `Esc` to close Atlas.
- Keeps controls contained to the Atlas window while open.

## Current controls

- `Ctrl+M`: open Atlas
- `Esc`: close Atlas, or hide the world map overlay first
- `W`: toggle world map overlay
- Mouse drag: pan
- Mouse wheel: zoom toward cursor
- Arrow keys / numpad: pan
- `PgUp` / `PgDn`: switch layer
- `+` / `-`: zoom
- `Home` / numpad `5`: reset view

## Release notes / warnings

Atlas stores explored zone images on disk. Long-running games, very large explored areas, or deliberate attempts to map huge portions of the world may use substantial disk space.

## Pre-release checklist

- Playtest in a normal game for several hours.
- Confirm zone capture works through ordinary exploration.
- Confirm underground exploration creates useful stitched maps.
- Confirm layer switching behaves clearly when a layer has no captured tiles.
- Confirm mouse pan and zoom remain comfortable at close and far zoom.
- Confirm world map overlay works underground.
- Confirm `Esc` returns cleanly to normal game input.
- Confirm no unexplored areas are revealed.
- Check approximate disk usage after a realistic session.
- Capture workshop screenshots from normal play.
- Create workshop cover / preview art.
- Write README and Steam Workshop description.
- First workshop upload to obtain the Workshop ID.
- Update `workshop.json` after the Workshop ID exists.

## Future ideas, not required for first release

- Optional world map overlay polish.
- Optional cache-size tooling or cache cleanup helper.
- Optional additional map markers.
- Optional UI refinements after player feedback.
- Compatibility maintenance if Caves of Qud rendering internals change.