# CoQ Auto-Map

Experimental automap mod for Caves of Qud. It captures explored zones as images and stitches them together by world, layer, and position, creating a continuous map of explored areas you can view. Adds a toggled world map overlay so players can tell where they are on the surface while underground.

## Implemented

- Render zones to .png image that can be saved to disk
- Images saved in the player's active save dir under ...\automap\tiles. Steam cloud will accept them.
- Zone image captured on automap open and when leaving zone.
- Unexplored areas are not revealed and are black
- On the automap unexplored areas are rendered without any shading for clarity. This does not reveal anything extra to the player they could not already see.
- Stand-alone UI window that displays the automap, captures keyboard controls, and displays the current layer
- Automap controls: Pan(N/E/S/W), Bilinear Zoom(+/-), Center on Player, Switch Layer(+/-), Render current zone
- Controls are contained to automap window while it is open
- Zone images stitched together at correct world positions on map plane and presented in automap window
- World map overlay that highlights current parasang player is in

## Desired refinements

- Clean up old dummy-grid prototype code.
- Improve UI layout, style, and labels
- Add better status text for tile count, layer, zoom, and cache path.
- Tune zoom and pan feel.
- Add mouse support for pan and zoom
- Add optional world map pan/zoom or a better fixed full-map view.
- Add cache management or a warning about disk usage.
- Add config/keybinding support.
- Add better documentation for players.
- Do a code cleanup pass before release.