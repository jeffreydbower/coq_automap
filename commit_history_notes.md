# Commit History

- 1: 7/11/26 ~8pm

Initial Commit of automap prototype code to Github. project directory named coq_automap.

- 2: 7/14/26 ~9:48pm

Renamed "Poc" from all mentiones in code since thisis not a proof of concept anymore.

- 3: 7/14/26 ~10:35pm

Removed debug and error logging untill errors start occuring.

- 4: 7/14/26 ~10:44pm

Removed code for dummy grid.


- 5: 7/14/26 ~10:58pm

Added comments above important functions and within EnsureUICreated() to document the UI elements.

- 6: 7/15/26 ~9:26pm

Moved world map code from EnsureUICreated to its own function 

- 7: 7/15/26 ~9:46pm

Moved UI helper functions out of EnsureUICreated

- 8: 7/15/26 ~9:55pm

Extract automap UI shell setup

- 9: 7/16/26 ~7:52pm

removed re-rended function (R key)
Added comments

- 10: 7/16/26 ~8:30pm

removed legacy render path

- 10: 7/16/26 ~10:25pm

Moved UI code to AutomapUiBuilder.cs

- 11: 7/17/26 ~8:43am

Moved Zone Cordinate helpers to  AutomapZoneCoord.cs

- 12: 7/17/26 ~9:00am

Moved Zone tile display to  AutomapTileDisplay.cs

- 13: 7/17/26 ~9:34am

Moved Zone tile rendering to file code  AutomapTileRenderer.cs

- 14: 7/17/26 ~9:49am

Split automap world map overlay  AutomapWorldMapOverlay.cs

- 15: 7/17/26 ~9:58am

Split automap input gate  AutomapInputgate.cs

- 16: 7/17/26 ~10:05am

Split automap bootstrap  AutomapBootstrap.cs

- 17: 7/17/26 ~10:19am

Split automap window lifecycle control  AutomapWindowLifecycle.cs

- 18: 7/17/26 ~10:29am

Split automap view controls  AutomapViewControls.cs

- 19: 7/17/26 ~10:59am
Cleaned Automap controller core AutomapController.cs, and other small clean ups in other files

- 20: 7/17/26 ~9:48
Consolodated multiple files.

- 21: 7/18/26 ~12:28pm
First UI pass. Commit before flair is added.

- 22: 7/18/26 ~12:28pm
UI Release Candidate done.

- 23: 7/18/26 ~3:37pm
Mouse grap-pan and to-cursor zoom implemented.