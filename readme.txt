[h1]Atlas of Qud — v1.0.3[/h1]

This mod is a cartographic assistant for mapping your journey through the Caves of Qud. Atlas of Qud contains a map of every area you visit while preserving exploration status. It also features a pull-up world map that pinpoints your location, even in the subterranean depths. See the Caves of Qud from a new perspective!

[h2]Game Features[/h2]

~ Automatically builds an Atlas as you explore.
~ View a stitched map of the zones you have visited.
~ Switch between surface, sky, and subterranean layers.
~ Pan and zoom with the mouse or keyboard.
~ Open a world map overlay to pinpoint your current parasang, even underground.
~ View a marker on the world map overlay showing the parasang currently centered in the Atlas viewport.
~ Shows previously explored but not-currently-visible areas clearly instead of greying them out.
~ Preserves exploration status and does not reveal unexplored areas.
~ Stores maps per save, so each journey has its own Atlas.

[h2]Controls[/h2]

~ Open Atlas: Ctrl+M
~ Close Atlas | Close world map overlay: Esc
~ Toggle World Map Overlay: W
~ Select Layer: PgUp / PgDn
~ Pan: Arrow keys | Numpad | Mouse Drag
~ Zoom: + / - | Mouse Wheel
~ Center on player: Home

[h2]Atlas of Qud Saves Images in Your Game’s Save Directory[/h2]

Atlas saves map images for explored zones in your save/cache directory. Normal play should be manageable, but very long games, heavy exploration, or deliberate attempts to map huge areas can use substantial disk space.

In testing, full-size zone images usually ranged from about 30 KB to 100 KB. Using a rough average of 65 KB per zone, 100 visited zones would use about 6.5 MB, 1,000 visited zones would use about 65 MB, and 10,000 visited zones would use about 650 MB.

A complete JoppaWorld layer is 80 × 25 parasangs, with 3 × 3 local zones per parasang, for 18,000 local zones on one Z-level.

For v1.0.3 testing, I stress-tested Atlas with a synthetic complete JoppaWorld layer, which is 18,000 local zone images on one Z-level, plus generated thumbnails. That test image set totaled about 1.4 GB, and Atlas remained usable after the thumbnail layer loaded.

As a rough real-play estimate, a long game may eventually capture a few thousand zones, depending heavily on play style and how much you explore.

[h2]Install and Uninstall Behavior[/h2]

Atlas can be safely removed by unsubscribing from the mod. Existing saves should remain playable, but Atlas map images are stored separately in your save/cache directory and may remain after the mod is removed.

On Windows/Steam, Caves of Qud saves are usually located here:

%USERPROFILE%\AppData\LocalLow\Freehold Games\CavesOfQud\Synced\Saves

Inside the save you want to clean up, look for:

Automap\tiles

You can delete the Automap folder, or just the tiles folder, to remove Atlas’s saved zone images for that game.

[h2]Development[/h2]

[url=https://github.com/jeffreydbower/coq_automap]View the source code on GitHub[/url]

[h2]Inspired by Caves of Qud World Map Web Viewer by kernelmethod[/h2]

[url=https://kernelmethod.org/notes/qud_worldmap/]Caves of Qud World Map Web Viewer[/url]

[h2]Updates[/h2]

v1.0.0
- Initial release.

v1.0.1
- Improved performance for large map areas. Layer switching and zoomed-out views now load visible map tiles progressively, prioritizing the center of the current view.

v1.0.2 
- Improved performance while panning and zooming large Atlas layers.
- Atlas now keeps loaded tiles for the active layer instead of unloading them when they leave the viewport.
- Large layers still load progressively, prioritizing tiles nearest the center of view.

v1.0.3
- Added a thumbnail layer that Atlas switches to when zoomed out. This greatly improves performance with potentially thousands of zones on screen while preserving zoomed-out readability.
- Added a marker on the world map overlay showing the parasang currently centered in the Atlas viewport.

[h2]This mod works great with my other Caves of Qud mod: Subterranean Sites.[/h2]

[url=https://steamcommunity.com/sharedfiles/filedetails/?id=3734789909]Subterranean Sites[/url]

https://steamcommunity.com/sharedfiles/filedetails/?id=3734789909

Tags: Map, World, Script, UX