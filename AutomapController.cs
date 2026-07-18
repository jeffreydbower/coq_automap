using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XRL;
using XRL.UI;
using XRL.World;
using ConsoleLib.Console;
using UnityEngine;
using XRL.World.Capabilities;
using Kobold;
using System.IO;
using UnityEngine;

using NavigationContext = XRL.UI.Framework.NavigationContext;

namespace CoQAutoMap
{
    //#####################################################    
    //Zone ID parser and coordinate helpers
    //#####################################################
    internal struct AutomapZoneCoord
    {
        public string World;
        public int ParasangX;
        public int ParasangY;
        public int ZoneX;
        public int ZoneY;
        public int Z;

        public int GlobalZoneX
        {
            get { return ParasangX * 3 + ZoneX; }
        }

        public int GlobalZoneY
        {
            get { return ParasangY * 3 + ZoneY; }
        }

        public string ZoneId
        {
            get
            {
                return
                    World + "." +
                    ParasangX + "." +
                    ParasangY + "." +
                    ZoneX + "." +
                    ZoneY + "." +
                    Z;
            }
        }

        public static bool TryParse(string zoneId, out AutomapZoneCoord coord)
        {
            coord = default(AutomapZoneCoord);

            if (string.IsNullOrEmpty(zoneId))
            {
                return false;
            }

            string[] parts = zoneId.Split('.');

            if (parts.Length != 6)
            {
                return false;
            }

            int parasangX;
            int parasangY;
            int zoneX;
            int zoneY;
            int z;

            if (!int.TryParse(parts[1], out parasangX))
            {
                return false;
            }

            if (!int.TryParse(parts[2], out parasangY))
            {
                return false;
            }

            if (!int.TryParse(parts[3], out zoneX))
            {
                return false;
            }

            if (!int.TryParse(parts[4], out zoneY))
            {
                return false;
            }

            if (!int.TryParse(parts[5], out z))
            {
                return false;
            }

            coord.World = parts[0];
            coord.ParasangX = parasangX;
            coord.ParasangY = parasangY;
            coord.ZoneX = zoneX;
            coord.ZoneY = zoneY;
            coord.Z = z;

            return true;
        }
    }

    public sealed partial class AutomapController : MonoBehaviour
    {

        //#####################################################  
        //Automap Controller Core    
        //Root singleton/install/update loop:
        //EnsureInstalled, Update, QueueDeactivatedZoneCapture, IsOpen, DebugLog
        //#####################################################

        private const string ControllerName = "CoQAutoMap_Controller";
        private static AutomapController _instance;
        private bool _isOpen;
        private bool _suppressToggleUntilReleased;

        public static void QueueDeactivatedZoneCapture(Zone zone, string source)
        {
            if (_instance == null)
            {
                return;
            }

            try
            {
                _instance.StartCaptureZoneImage(
                    zone,
                    source + " auto-capture",
                    loadWhenComplete: false
                );
            }
            catch (Exception ex)
            {
                if (_instance._isOpen)
                {
                    _instance.SetCaptureStatus(
                        source +
                        ": auto-capture failed: " +
                        ex.GetType().Name +
                        ": " +
                        ex.Message
                    );
                }
            }
        }

        public static void DebugLog(string message)
        {
            // Debug logging disabled for normal use.
        }

        public static bool IsOpen
        {
            get
            {
                return _instance != null && _instance._isOpen;
            }
        }

        public static void EnsureInstalled(string source)
        {
            try
            {
                if (_instance != null)
                {
                    return;
                }

                UnityEngine.GameObject existing = UnityEngine.GameObject.Find(ControllerName);

                if (existing != null)
                {
                    _instance = existing.GetComponent<AutomapController>();

                    if (_instance != null)
                    {
                        return;
                    }
                }

                UnityEngine.GameObject controllerObject = new UnityEngine.GameObject(ControllerName);
                DontDestroyOnLoad(controllerObject);

               _instance = controllerObject.AddComponent<AutomapController>();
               

                AutomapInputGate.Install();

                try
                {
                    The.Game?.RequireSystem<AutomapZoneCaptureSystem>();
                }
                catch (Exception ex)
                {
                    Popup.Show(
                        "CoQ Auto-Map zone capture system install exception:\n\n" +
                        ex.GetType().Name +
                        "\n" +
                        ex.Message
                    );
                }

                //Popup.Show("CoQ Auto-Map NavigationContext loaded.\n\nPress Ctrl+M to open the Automap window.");
            }
            catch (Exception ex)
            {
                Popup.Show("CoQ Auto-Map install exception:\n\n" + ex.GetType().Name + "\n" + ex.Message);
            }
        }

        // Unity calls Update once per frame while the controller GameObject exists.
        // This is the small runtime loop for the automap:
        // - release temporary input blocks after Ctrl+M/Escape are physically released
        // - complete any queued zone-capture work
        // - detect Ctrl+M and route automap controls while the window is open
        private void Update()
        {
            try
            {
                AutomapInputGate.UpdateReleaseBlocks();

                PollZoneCapture();

                bool ctrlDown =
                    Input.GetKey(UnityEngine.KeyCode.LeftControl) ||
                    Input.GetKey(UnityEngine.KeyCode.RightControl);

                bool ctrlMDown = ctrlDown && Input.GetKeyDown(UnityEngine.KeyCode.M);

                if (_suppressToggleUntilReleased)
                {
                    bool stillHeld =
                        Input.GetKey(UnityEngine.KeyCode.LeftControl) ||
                        Input.GetKey(UnityEngine.KeyCode.RightControl) ||
                        Input.GetKey(UnityEngine.KeyCode.M);

                    if (stillHeld)
                    {
                        return;
                    }

                    _suppressToggleUntilReleased = false;
                }

                // Keep Ctrl+M as a raw Unity bootstrap hotkey for now.
                // All Automap controls after opening should be handled through Qud's NavigationContext.
                if (ctrlMDown)
                {
                    if (_isOpen)
                    {
                        CloseWindow("Ctrl+M raw toggle");
                    }
                    else
                    {
                        OpenWindow("Ctrl+M raw toggle");
                    }

                    _suppressToggleUntilReleased = true;
                    return;
                }

                if (_isOpen)
                {
                    HandleRawAutomapControls();
                }
            }
            catch (Exception ex)
            {
                if (_isOpen)
                {
                    SetCaptureStatus(
                        "Automap update failed: " +
                        ex.GetType().Name +
                        ": " +
                        ex.Message
                    );
                }
            }
        }

        //#####################################################
        // Zone capture/render pipeline
        // StartCaptureZoneImage, CaptureZoneToPngQueued, PollZoneCapture  
        //#####################################################

        private bool _capturePending;
        private bool _captureComplete;
        private bool _captureLoadWhenComplete;

        private string _capturePath;
        private string _captureError;
        private DateTime _captureStartTime;

        private void StartCaptureCurrentZoneImage(string source)
        {
            try
            {
                Zone zone = The.Player?.GetCurrentZone();

                if (zone == null)
                {
                    SetCaptureStatus(source + ": no current zone found.");
                    return;
                }

                StartCaptureZoneImage(
                    zone,
                    source,
                    loadWhenComplete: true
                );
            }
            catch (Exception ex)
            {
                _capturePending = false;
                _captureComplete = false;
                _captureError = ex.ToString();

                SetCaptureStatus(source + ": capture start exception: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        // Starts an asynchronous-ish zone capture.
        // The zone's cell/render data is read here, but Unity texture creation and PNG
        // encoding are queued onto Qud's UI thread by CaptureZoneToPngQueued(...).
        // PollZoneCapture() later notices when that queued work is done and refreshes
        // the visible stitched map if needed.
        private void StartCaptureZoneImage(Zone zone, string source, bool loadWhenComplete)
        {
            try
            {
                if (_capturePending)
                {

                    return;
                }

                if (zone == null)
                {
                    if (loadWhenComplete)
                    {
                        SetCaptureStatus(source + ": no zone supplied.");
                    }

                    return;
                }

                string zoneId = zone.ZoneID ?? "UnknownZone";

                if (!zoneId.Contains("."))
                {
                    return;
                }

                if (zone.Stale)
                {
                    return;
                }

                string safeZoneId = MakeSafeFileName(zoneId);

                string automapDir = GetAutomapTileDirectory();
                Directory.CreateDirectory(automapDir);

                string savePath = Path.Combine(automapDir, safeZoneId + ".png");

                _capturePending = true;
                _captureComplete = false;
                _captureLoadWhenComplete = loadWhenComplete;
                _capturePath = savePath;
                _captureError = null;
                _captureStartTime = DateTime.Now;

                string message =
                    "Rendering zone: " +
                    zoneId +
                    " → " +
                    savePath +
                    " loadWhenComplete=" +
                    loadWhenComplete +
                    " stale=" +
                    zone.Stale +
                    " suspended=" +
                    zone.Suspended;

                if (loadWhenComplete)
                {
                    SetCaptureStatus(message);
                }

                CaptureZoneToPngQueued(zone, savePath);
            }
            catch (Exception ex)
            {
                _capturePending = false;
                _captureComplete = false;
                _captureError = ex.ToString();

                if (loadWhenComplete)
                {
                    SetCaptureStatus(source + ": capture start exception: " + ex.GetType().Name + " " + ex.Message);
                }
            }
        }

        // Captures a Qud zone into a PNG tile used by the stitched automap.
        // The pixel-coloring approach is adapted from the Qud WorldMap Viewer style:
        // render each cell, read the tile sprite, then map transparent/dark/light sprite
        // pixels to background/foreground/detail colors.
        // This keeps the automap visually close to Qud's own tile rendering while letting
        // us control explored-but-not-visible shading.
        //Qud World-Map Viewer https://kernelmethod.org/notes/qud_worldmap/
        //https://github.com/kernelmethod/Qud-WorldMap-Viewer
        private void CaptureZoneToPngQueued(Zone zone, string savePath)
        {

            try
            {
                int zoneWidth = zone.Width;
                int zoneHeight = zone.Height;

                SnapshotRenderable[,] cells = new SnapshotRenderable[zoneWidth, zoneHeight];
                bool[,] exploredMap = new bool[zoneWidth, zoneHeight];

                // Use Qud's current exploration, visibility, and light state.
                for (int y = 0; y < zoneHeight; y++)
                {
                    for (int x = 0; x < zoneWidth; x++)
                    {
                        Cell cell = zone.GetCell(x, y);

                        bool explored = cell.IsExplored();
                        bool visible = cell.IsVisible();
                        LightLevel lightLevel = cell.GetLight();

                        exploredMap[x, y] = explored;
                        //custom cell rendering path that removes explored shading
                        RenderEvent rendered = AutomapCellRenderer.RenderCellForAutomap(
                            cell,
                            visible,
                            lightLevel,
                            explored
                        );

                        SnapshotRenderable snapshot = rendered == null
                            ? null
                            : new SnapshotRenderable(rendered);

                        cells[x, y] = snapshot;
                    }
                }

                GameManager.Instance.uiQueue.queueTask(delegate
                {
                    Texture2D mapTexture = null;

                    try
                    {
                        int tileWidth = 16;
                        int tileHeight = 24;
                        int imageWidth = tileWidth * zoneWidth;
                        int imageHeight = tileHeight * zoneHeight;

                        mapTexture = new Texture2D(
                            imageWidth,
                            imageHeight,
                            TextureFormat.ARGB32,
                            mipChain: false
                        );

                        //mapTexture.filterMode = FilterMode.Point;
                        mapTexture.filterMode = UnityEngine.FilterMode.Point;

                        UnityEngine.Color baseColor =
                            ConsoleLib.Console.ColorUtility.FromWebColor("041312");

                        UnityEngine.Color[] mapPixels = mapTexture.GetPixels();

                        for (int p = 0; p < mapPixels.Length; p++)
                        {
                            mapPixels[p] = new UnityEngine.Color(0f, 0f, 0f, 0f);
                        }

                        for (int y = 0; y < zoneHeight; y++)
                        {
                            for (int x = 0; x < zoneWidth; x++)
                            {
                                if (!exploredMap[x, y])
                                {
                                    for (int px = 0; px < tileWidth; px++)
                                    {
                                        for (int py = 0; py < tileHeight; py++)
                                        {
                                            int imageX = x * tileWidth + px;
                                            int imageY = (zoneHeight - y - 1) * tileHeight + py;

                                            mapPixels[imageX + imageY * imageWidth] = UnityEngine.Color.black;
                                        }
                                    }

                                    continue;
                                }

                                SnapshotRenderable cellTile = cells[x, y];

                                if (cellTile == null)
                                {
                                    continue;
                                }

                                string spriteName = cellTile.GetSpriteName();

                                if (string.IsNullOrEmpty(spriteName))
                                {
                                    continue;
                                }

                                Sprite sprite = SpriteManager.GetUnitySprite(spriteName);

                                if (sprite == null || sprite.texture == null)
                                {
                                    continue;
                                }

                                UnityEngine.Color foregroundColor =
                                    ConsoleLib.Console.ColorUtility.colorFromChar(cellTile.GetForegroundColor());

                                UnityEngine.Color detailColor =
                                    ConsoleLib.Console.ColorUtility.colorFromChar(cellTile.getDetailColor());

                                for (int px = 0; px < tileWidth; px++)
                                {
                                    for (int py = 0; py < tileHeight; py++)
                                    {
                                        // Corrected to 0-based pixel coords.
                                        int spriteX = cellTile.getHFlip() ? tileWidth - px - 1 : px;
                                        int spriteY = cellTile.getVFlip() ? tileHeight - py - 1 : py;

                                        UnityEngine.Color spritePixel = sprite.texture.GetPixel(spriteX, spriteY);
                                        UnityEngine.Color pixelColor = baseColor;

                                        // Same basic rule as Qud-WorldMap-Viewer:
                                        // transparent = base background
                                        // dark sprite pixel = foreground
                                        // light sprite pixel = detail/background
                                        if (spritePixel.a <= 0f)
                                        {
                                            pixelColor = baseColor;
                                        }
                                        else if (spritePixel.r < 0.5f)
                                        {
                                            pixelColor = foregroundColor;
                                        }
                                        else
                                        {
                                            pixelColor = detailColor;
                                        }

                                        int imageX = x * tileWidth + px;
                                        int imageY = (zoneHeight - y - 1) * tileHeight + py;

                                        mapPixels[imageX + imageY * imageWidth] = pixelColor;
                                    }
                                }
                            }
                        }

                        mapTexture.SetPixels(mapPixels);
                        mapTexture.Apply(updateMipmaps: false);

                        byte[] pngBytes = mapTexture.EncodeToPNG();

                        Directory.CreateDirectory(Path.GetDirectoryName(savePath));
                        File.WriteAllBytes(savePath, pngBytes);
                    }
                    catch (Exception ex)
                    {
                        _captureError = ex.GetType().Name + ": " + ex.Message;
                    }
                    finally
                    {
                        if (mapTexture != null)
                        {
                            UnityEngine.Object.Destroy(mapTexture);
                        }

                        _captureComplete = true;
                    }
                });
            }
            catch (Exception ex)
            {
                _captureError = ex.GetType().Name + ": " + ex.Message;
                _captureComplete = true;
            }
        }

        // Zone capture finishes from a queued UI task because Unity texture work has to
        // happen on the UI/main thread. This method checks each frame whether the queued
        // capture is done, then reloads the stitched map if the visible automap requested it.
        private void PollZoneCapture()
        {
            if (!_capturePending)
            {
                return;
            }

            if (!_captureComplete)
            {
                return;
            }

            _capturePending = false;

            if (!string.IsNullOrEmpty(_captureError))
            {
                SetCaptureStatus("Zone render failed: " + _captureError);
                return;
            }

            if (string.IsNullOrEmpty(_capturePath) || !File.Exists(_capturePath))
            {
                string message = "Zone render finished but file was not found: " + Safe(_capturePath);

                if (_captureLoadWhenComplete)
                {
                    SetCaptureStatus(message);
                }

                return;
            }

            try
            {
                TimeSpan elapsed = DateTime.Now - _captureStartTime;

                if (!_captureLoadWhenComplete)
                {

                    return;
                }

                _displayZ = GetCurrentZoneZOrSurface();

                LoadCapturedZoneTilesForCurrentLayer("PollZoneCapture");

                SetCaptureStatus(
                    "Loaded automap tiles after zone render: " +
                    _capturePath +
                    " elapsed=" +
                    elapsed.TotalSeconds.ToString("0.000") +
                    "s"
                );
            }
            catch (Exception ex)
            {
                SetCaptureStatus("Load rendered zone exception: " + ex.GetType().Name + " " + ex.Message);
            }
        }

        private string GetAutomapTileDirectory()
        {
            try
            {
                if (The.Game != null)
                {
                    return The.Game.GetCacheDirectory(Path.Combine("Automap", "tiles"));
                }
            }
            catch
            {
            }

            return DataManager.SavePath(Path.Combine("Automap", "tiles"));
        }

        private static string MakeSafeFileName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "UnknownZone";
            }

            char[] invalid = Path.GetInvalidFileNameChars();

            for (int i = 0; i < invalid.Length; i++)
            {
                value = value.Replace(invalid[i], '_');
            }

            return value;
        }
        
        //#####################################################    
        // Loaded PNG tile display:
        // LoadCapturedZoneTilesForCurrentLayer, RefreshMapTiles, ApplyMapPlaneTransform
        //#####################################################

        private readonly List<UnityEngine.GameObject> _loadedZoneTileObjects = new List<UnityEngine.GameObject>();
        private readonly List<Texture2D> _loadedZoneTileTextures = new List<Texture2D>();

        private void ClearLoadedZoneTiles()
        {
            for (int i = 0; i < _loadedZoneTileObjects.Count; i++)
            {
                UnityEngine.GameObject tileObject = _loadedZoneTileObjects[i];

                if (tileObject != null)
                {
                    UnityEngine.Object.Destroy(tileObject);
                }
            }

            _loadedZoneTileObjects.Clear();

            for (int i = 0; i < _loadedZoneTileTextures.Count; i++)
            {
                Texture2D texture = _loadedZoneTileTextures[i];

                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            _loadedZoneTileTextures.Clear();
        }

        private void LoadCapturedZoneTilesForCurrentLayer(string source)
        {
            try
            {
                EnsureUiCreated();

                if (_zoneTileContainer == null)
                {
                    SetCaptureStatus(source + ": no zone tile container.");
                    return;
                }

                Zone currentZone = The.Player?.GetCurrentZone();

                if (currentZone == null)
                {
                    SetCaptureStatus(source + ": no current zone for tile placement.");
                    return;
                }

                AutomapZoneCoord currentCoord;

                if (!AutomapZoneCoord.TryParse(currentZone.ZoneID, out currentCoord))
                {
                    SetCaptureStatus(source + ": could not parse current zone ID: " + Safe(currentZone.ZoneID));
                    return;
                }

                EnsureDisplayZInitialized();

                int displayZ = _displayZ;

                if (_layerText != null)
                {
                    // GetFormattedLayerName is in AutomapUiBuilder.cs
                    _layerText.text = GetFormattedLayerName(displayZ);
                }


                string automapDir = GetAutomapTileDirectory();

                if (!Directory.Exists(automapDir))
                {
                    SetCaptureStatus(source + ": automap tile directory does not exist: " + automapDir);
                    return;
                }

                ClearLoadedZoneTiles();

                string[] pngFiles = Directory.GetFiles(automapDir, "*.png");

                int loadedCount = 0;
                int skippedCount = 0;
                

                for (int i = 0; i < pngFiles.Length; i++)
                {
                    string path = pngFiles[i];
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);

                    AutomapZoneCoord tileCoord;

                    // Tile filenames are saved from Qud zone IDs:
                    //   World.ParasangX.ParasangY.ZoneX.ZoneY.Z.png
                    //
                    // A parasang contains a 3x3 block of local zones, so AutomapZoneCoord converts
                    // parasang/local coordinates into global zone coordinates:
                    //
                    //   GlobalZoneX = ParasangX * 3 + ZoneX
                    //   GlobalZoneY = ParasangY * 3 + ZoneY
                    //
                    // The stitched automap is centered on the player's current zone. Each loaded
                    // tile is placed by comparing its global zone coordinate to the current zone's
                    // global coordinate, then multiplying by the rendered PNG size.

                    if (!AutomapZoneCoord.TryParse(fileNameWithoutExtension, out tileCoord))
                    {
                        skippedCount++;
                        continue;
                    }

                    if (tileCoord.World != currentCoord.World)
                    {
                        skippedCount++;
                        continue;
                    }

                    if (tileCoord.Z != displayZ)
                    {
                        skippedCount++;
                        continue;
                    }

                    byte[] bytes = File.ReadAllBytes(path);

                    Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);

                    if (!texture.LoadImage(bytes))
                    {
                        UnityEngine.Object.Destroy(texture);
                        skippedCount++;
                        continue;
                    }

                    //texture.filterMode = UnityEngine.FilterMode.Point;
                    //texture.wrapMode = TextureWrapMode.Clamp;

                    texture.filterMode = UnityEngine.FilterMode.Bilinear;
                    texture.wrapMode = TextureWrapMode.Clamp;

                    _loadedZoneTileTextures.Add(texture);

                    UnityEngine.GameObject tileObject = new UnityEngine.GameObject(
                        "ZoneTile_" + fileNameWithoutExtension
                    );

                    tileObject.transform.SetParent(_zoneTileContainer.transform, false);

                    RectTransform tileRect = tileObject.AddComponent<RectTransform>();
                    tileRect.anchorMin = new Vector2(0.5f, 0.5f);
                    tileRect.anchorMax = new Vector2(0.5f, 0.5f);
                    tileRect.pivot = new Vector2(0.5f, 0.5f);

                    tileRect.sizeDelta = new Vector2(texture.width, texture.height);

                    int relativeZoneX = tileCoord.GlobalZoneX - currentCoord.GlobalZoneX;
                    int relativeZoneY = tileCoord.GlobalZoneY - currentCoord.GlobalZoneY;


                    // Unity UI Y coordinates increase upward, while Qud/world map Y increases
                    // downward/southward, so relativeZoneY is negated when placing the tile.
                    tileRect.anchoredPosition = new Vector2(
                        relativeZoneX * texture.width,
                        -relativeZoneY * texture.height
                    );

                    RawImage rawImage = tileObject.AddComponent<RawImage>();
                    rawImage.texture = texture;
                    rawImage.color = Color.white;
                    rawImage.raycastTarget = false;

                    _loadedZoneTileObjects.Add(tileObject);

                    loadedCount++;
                }

                ApplyMapPlaneTransform();

                SetCaptureStatus(
                    source +
                    ": loaded " +
                    loadedCount +
                    " automap tile(s) for " +
                    currentCoord.World +
                    " | Layer: " +
                    GetLayerLabel(displayZ) +
                    " | Center: " +
                    currentCoord.ZoneId +
                    " | skipped=" +
                    skippedCount
                );

            }
            catch (Exception ex)
            {
                SetCaptureStatus(
                    source +
                    ": tile load failed: " +
                    ex.GetType().Name +
                    ": " +
                    ex.Message
                );
            }
        }

          private void ApplyMapPlaneTransform()
        {
            if (_mapPlane == null)
            {
                return;
            }

            _mapPlane.anchoredPosition = _mapPlaneOffset;
            _mapPlane.localScale = new Vector3(_zoom, _zoom, 1f);
        }

        private void RefreshMapTiles(string source)
        {
            EnsureDisplayZInitialized();

            ApplyMapPlaneTransform();

            if (_layerText != null)
            {
                //GetFormattedLayerName is in AutomapUiBuilder.cs
                _layerText.text = GetFormattedLayerName(_displayZ);
            }

            if (_statusText != null)
            {
                _statusText.text =
                    "Automap" +
                    " | Pan: (" + _panX + ", " + _panY + ")" +
                    " | Offset: (" +
                    _mapPlaneOffset.x.ToString("0") +
                    ", " +
                    _mapPlaneOffset.y.ToString("0") +
                    ")" +
                    " | Zoom: " + _zoom.ToString("0.00") +
                    " | Layer: " + GetLayerLabel(_displayZ) +
                    " | Source: " + source;
            }
        }

        private static string Safe(string value)
        {
            return value ?? "<null>";
        }

        private void SetCaptureStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }
            //Log(message);
        }




    }
}