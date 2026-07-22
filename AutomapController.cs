using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XRL;
using XRL.UI;
using XRL.World;
using ConsoleLib.Console;
using XRL.World.Capabilities;
using Kobold;
using System.IO;


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

                if (!_isOpen && Input.GetKeyDown(UnityEngine.KeyCode.M) &&
                    (
                        Input.GetKey(UnityEngine.KeyCode.LeftControl) ||
                        Input.GetKey(UnityEngine.KeyCode.RightControl)
                    ))
                {
                    OpenWindow("Ctrl+M raw open");
                    return;
                }

                if (_isOpen)
                {
                    HandleRawAutomapControls();
                    ProcessPendingVisibleTileLoads();
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

        private string _thumbnailRepairCheckedSaveKey;

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

                        GenerateThumbnailForFullTile(savePath);
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

        // Zone capture finishes from a queued UI task because Unity texture work has to//
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

            MarkTileIndexDirty();
            ClearLoadedZoneTiles();

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

        private bool CurrentZoneTileExists()
        {
            try
            {
                Zone zone = The.Player?.GetCurrentZone();

                if (zone == null)
                {
                    return false;
                }

                string zoneId = zone.ZoneID;

                if (string.IsNullOrEmpty(zoneId))
                {
                    return false;
                }

                AutomapZoneCoord coord;

                if (!AutomapZoneCoord.TryParse(zoneId, out coord))
                {
                    return false;
                }

                string automapDir = GetAutomapTileDirectory();

                if (string.IsNullOrEmpty(automapDir))
                {
                    return false;
                }

                string safeZoneId = MakeSafeFileName(zoneId);
                string tilePath = Path.Combine(automapDir, safeZoneId + ".png");

                return File.Exists(tilePath);
            }
            catch
            {
                return false;
            }
        }

       private static bool IsThumbnailPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string fileName = Path.GetFileName(path);

            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            return fileName.StartsWith(
                AutomapThumbPrefix,
                StringComparison.OrdinalIgnoreCase
            );
        }

        private static string GetThumbnailPath(string fullTilePath)
        {
            if (string.IsNullOrEmpty(fullTilePath))
            {
                return "";
            }

            if (IsThumbnailPath(fullTilePath))
            {
                return fullTilePath;
            }

            string directory = Path.GetDirectoryName(fullTilePath);
            string fileName = Path.GetFileName(fullTilePath);

            if (string.IsNullOrEmpty(directory) ||
                string.IsNullOrEmpty(fileName))
            {
                return "";
            }

            return Path.Combine(
                directory,
                AutomapThumbPrefix + fileName
            );
        }

        private static bool IsThumbnailFresh(string fullTilePath)
        {
            if (string.IsNullOrEmpty(fullTilePath))
            {
                return false;
            }

            if (!File.Exists(fullTilePath))
            {
                return false;
            }

            string thumbPath = GetThumbnailPath(fullTilePath);

            if (string.IsNullOrEmpty(thumbPath))
            {
                return false;
            }

            if (!File.Exists(thumbPath))
            {
                return false;
            }

            try
            {
                DateTime fullWriteTime = File.GetLastWriteTimeUtc(fullTilePath);
                DateTime thumbWriteTime = File.GetLastWriteTimeUtc(thumbPath);

                return thumbWriteTime >= fullWriteTime;
            }
            catch
            {
                return false;
            }
        }

        private static bool GenerateThumbnailForFullTile(string fullTilePath)
        {
            if (string.IsNullOrEmpty(fullTilePath))
            {
                return false;
            }

            if (IsThumbnailPath(fullTilePath))
            {
                return false;
            }

            AutomapZoneCoord ignoredCoord;

            if (!TryGetFullTileCoordFromPath(fullTilePath, out ignoredCoord))
            {
                return false;
            }

            if (!File.Exists(fullTilePath))
            {
                return false;
            }

            string thumbPath = GetThumbnailPath(fullTilePath);

            if (string.IsNullOrEmpty(thumbPath))
            {
                return false;
            }

            Texture2D fullTexture = null;
            Texture2D thumbTexture = null;

            try
            {
                byte[] fullBytes = File.ReadAllBytes(fullTilePath);

                fullTexture = new Texture2D(
                    2,
                    2,
                    TextureFormat.ARGB32,
                    mipChain: false
                );

                if (!fullTexture.LoadImage(fullBytes))
                {
                    return false;
                }

                fullTexture.filterMode = UnityEngine.FilterMode.Bilinear;
                fullTexture.wrapMode = TextureWrapMode.Clamp;

                thumbTexture = new Texture2D(
                    AutomapThumbPixelWidth,
                    AutomapThumbPixelHeight,
                    TextureFormat.ARGB32,
                    mipChain: false
                );

                UnityEngine.Color[] thumbPixels =
                    new UnityEngine.Color[AutomapThumbPixelWidth * AutomapThumbPixelHeight];

                for (int y = 0; y < AutomapThumbPixelHeight; y++)
                {
                    for (int x = 0; x < AutomapThumbPixelWidth; x++)
                    {
                        float u =
                            (x + 0.5f) /
                            AutomapThumbPixelWidth;

                        float v =
                            (y + 0.5f) /
                            AutomapThumbPixelHeight;

                        thumbPixels[x + y * AutomapThumbPixelWidth] =
                            fullTexture.GetPixelBilinear(u, v);
                    }
                }

                thumbTexture.SetPixels(thumbPixels);
                thumbTexture.Apply(updateMipmaps: false);

                byte[] thumbBytes = thumbTexture.EncodeToPNG();

                Directory.CreateDirectory(Path.GetDirectoryName(thumbPath));
                File.WriteAllBytes(thumbPath, thumbBytes);

                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (fullTexture != null)
                {
                    UnityEngine.Object.Destroy(fullTexture);
                }

                if (thumbTexture != null)
                {
                    UnityEngine.Object.Destroy(thumbTexture);
                }
            }
        }

        private bool RepairThumbnailCache(string source)
        {
            try
            {
                string automapDir = GetAutomapTileDirectory();

                if (string.IsNullOrEmpty(automapDir))
                {
                    return false;
                }

                if (!Directory.Exists(automapDir))
                {
                    return true;
                }

                string[] pngFiles = Directory.GetFiles(automapDir, "*.png");

                int checkedCount = 0;
                int repairedCount = 0;
                int skippedCount = 0;
                int failedCount = 0;

                for (int i = 0; i < pngFiles.Length; i++)
                {
                    string path = pngFiles[i];

                    AutomapZoneCoord ignoredCoord;

                    if (!TryGetFullTileCoordFromPath(path, out ignoredCoord))
                    {
                        skippedCount++;
                        continue;
                    }

                    checkedCount++;

                    if (IsThumbnailFresh(path))
                    {
                        continue;
                    }

                    if (GenerateThumbnailForFullTile(path))
                    {
                        repairedCount++;
                    }
                    else
                    {
                        failedCount++;
                    }
                }

                if (repairedCount > 0)
                {
                    MarkTileIndexDirty();
                }

                SetCaptureStatus(
                    source +
                    ": thumbnail cache checked " +
                    checkedCount +
                    " full tile(s), repaired " +
                    repairedCount +
                    ", failed " +
                    failedCount +
                    ", skipped " +
                    skippedCount +
                    " non-full-tile PNG(s)"
                );

                return failedCount == 0;
            }
            catch (Exception ex)
            {
                SetCaptureStatus(
                    source +
                    ": thumbnail cache repair failed: " +
                    ex.GetType().Name +
                    ": " +
                    ex.Message
                );

                return false;
            }
        }

        private bool ThumbnailCacheNeedsRepair()
        {
            try
            {
                string automapDir = GetAutomapTileDirectory();

                if (string.IsNullOrEmpty(automapDir))
                {
                    return false;
                }

                if (!Directory.Exists(automapDir))
                {
                    return false;
                }

                string[] pngFiles = Directory.GetFiles(automapDir, "*.png");

                for (int i = 0; i < pngFiles.Length; i++)
                {
                    string path = pngFiles[i];

                    AutomapZoneCoord ignoredCoord;

                    if (!TryGetFullTileCoordFromPath(path, out ignoredCoord))
                    {
                        continue;
                    }

                    if (!IsThumbnailFresh(path))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private string GetThumbnailRepairSessionSaveKey()
        {
            try
            {
                if (The.Game == null)
                {
                    return null;
                }

                string tileDir = GetAutomapTileDirectory();

                if (string.IsNullOrEmpty(tileDir))
                {
                    return null;
                }

                return tileDir;
            }
            catch
            {
                return null;
            }
        }

        private bool RepairThumbnailCacheIfNeededThisSession(string source)
        {
            string saveKey = GetThumbnailRepairSessionSaveKey();

            if (string.IsNullOrEmpty(saveKey))
            {
                return false;
            }

            if (string.Equals(
                _thumbnailRepairCheckedSaveKey,
                saveKey,
                StringComparison.OrdinalIgnoreCase
            ))
            {
                return false;
            }

            _thumbnailRepairCheckedSaveKey = saveKey;

            if (!ThumbnailCacheNeedsRepair())
            {
                return false;
            }

            int checkedCount;
            int repairedCount;
            int failedCount;
            int skippedCount;

            bool repaired = RepairThumbnailCache(
                source,
                out checkedCount,
                out repairedCount,
                out failedCount,
                out skippedCount
            );

            if (!repaired)
            {
                _thumbnailRepairCheckedSaveKey = null;
            }

            string message =
                "Atlas of Qud prepared thumbnail map images for this save.\n\n" +
                "Checked " +
                checkedCount +
                " full tile(s).\n" +
                "Created or updated " +
                repairedCount +
                " thumbnail tile(s).\n";

            if (skippedCount > 0)
            {
                message +=
                    "Skipped " +
                    skippedCount +
                    " non-full-tile PNG(s).\n";
            }

            if (failedCount > 0)
            {
                message +=
                    "\n" +
                    failedCount +
                    " thumbnail tile(s) could not be prepared. Atlas will try again next time.";
            }
            else
            {
                message +=
                    "\nOpen Atlas again to continue.";
            }

            Popup.Show(message);

            return true;
        }

        private static bool TryGetFullTileCoordFromPath(
            string path,
            out AutomapZoneCoord coord
        )
        {
            coord = default(AutomapZoneCoord);

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            if (IsThumbnailPath(path))
            {
                return false;
            }

            if (!string.Equals(
                Path.GetExtension(path),
                ".png",
                StringComparison.OrdinalIgnoreCase
            ))
            {
                return false;
            }

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);

            if (string.IsNullOrEmpty(fileNameWithoutExtension))
            {
                return false;
            }

            return AutomapZoneCoord.TryParse(fileNameWithoutExtension, out coord);
        }

        private bool RepairThumbnailCache(
            string source,
            out int checkedCount,
            out int repairedCount,
            out int failedCount,
            out int skippedCount
        )
        {
            checkedCount = 0;
            repairedCount = 0;
            failedCount = 0;
            skippedCount = 0;

            try
            {
                string automapDir = GetAutomapTileDirectory();

                if (string.IsNullOrEmpty(automapDir))
                {
                    return false;
                }

                if (!Directory.Exists(automapDir))
                {
                    return true;
                }

                string[] pngFiles = Directory.GetFiles(automapDir, "*.png");

                for (int i = 0; i < pngFiles.Length; i++)
                {
                    string path = pngFiles[i];

                    AutomapZoneCoord ignoredCoord;

                    if (!TryGetFullTileCoordFromPath(path, out ignoredCoord))
                    {
                        skippedCount++;
                        continue;
                    }

                    checkedCount++;

                    if (IsThumbnailFresh(path))
                    {
                        continue;
                    }

                    if (GenerateThumbnailForFullTile(path))
                    {
                        repairedCount++;
                    }
                    else
                    {
                        failedCount++;
                    }
                }

                if (repairedCount > 0)
                {
                    MarkTileIndexDirty();
                }

                SetCaptureStatus(
                    source +
                    ": thumbnail cache checked " +
                    checkedCount +
                    " full tile(s), repaired " +
                    repairedCount +
                    ", failed " +
                    failedCount +
                    ", skipped " +
                    skippedCount +
                    " non-full-tile PNG(s)"
                );

                return failedCount == 0;
            }
            catch (Exception ex)
            {
                SetCaptureStatus(
                    source +
                    ": thumbnail cache repair failed: " +
                    ex.GetType().Name +
                    ": " +
                    ex.Message
                );

                return false;
            }
        }

        public static void ResetThumbnailRepairSession(string source)
        {
            if (_instance == null)
            {
                return;
            }

            _instance._thumbnailRepairCheckedSaveKey = null;
        }

        //#####################################################    
        // Loaded PNG tile display:
        // lightweight filename index + progressive center-priority layer loading
        //#####################################################

        private sealed class AutomapKnownTile
        {
            public string Key;
            public string FullPath;
            public string ThumbnailPath;
            public AutomapZoneCoord Coord;
        }

        private sealed class AutomapLoadedTile
        {
            public AutomapKnownTile KnownTile;
            public Texture2D Texture;
            public UnityEngine.GameObject GameObject;
        }

        private const int AutomapTilePixelWidth = 1280;
        private const int AutomapTilePixelHeight = 600;

        private const int AutomapThumbPixelWidth = 192;
        private const int AutomapThumbPixelHeight = 90;
        private const string AutomapThumbPrefix = "thumb.";

        private enum TileResolutionMode
        {
            Full,
            Thumbnail
        }

        private const float ThumbnailModeEnterZoom = 0.18f;
        private const float ThumbnailModeExitZoom = 0.24f;

        private const int FullTileBufferZones = 1;
        private const int MaxThumbnailTileLoadsPerFrame = 256;

        private TileResolutionMode _tileResolutionMode = TileResolutionMode.Full;


        // Number of PNG tiles to decode/create per frame during progressive loading.
        // 24 was chosen from testing on an older (circa 2015) i5-6600K / GTX 970 machine:
        // low enough to avoid the old full-layer hitch, high enough that normal
        // visible Atlas views usually appear immediately.
        private const int MaxTileLoadsPerFrame = 24;

        private readonly Queue<AutomapKnownTile> _pendingVisibleTileLoads =
            new Queue<AutomapKnownTile>();

        private readonly HashSet<string> _pendingVisibleTileLoadKeys =
            new HashSet<string>();

        private readonly Queue<AutomapKnownTile> _pendingThumbnailTileLoads =
            new Queue<AutomapKnownTile>();

        private readonly HashSet<string> _pendingThumbnailTileLoadKeys =
            new HashSet<string>();

        private readonly Dictionary<string, AutomapKnownTile> _knownTilesByKey =
            new Dictionary<string, AutomapKnownTile>();

        private readonly Dictionary<string, AutomapLoadedTile> _visibleLoadedTilesByKey =
            new Dictionary<string, AutomapLoadedTile>();

        private readonly Dictionary<string, AutomapLoadedTile> _thumbnailLoadedTilesByKey =
            new Dictionary<string, AutomapLoadedTile>();

        private string _indexedTileDirectory;
        private string _indexedWorld;
        private int _indexedZ = int.MinValue;
        private bool _tileIndexDirty = true;

        private string _loadedTileCenterZoneId;
        private int _loadedTileZ = int.MinValue;

        private void MarkTileIndexDirty()
        {
            _tileIndexDirty = true;
        }

        private void ClearLoadedZoneTiles()
        {
            ClearPendingVisibleTileLoads();

            List<string> loadedFullKeys = new List<string>(_visibleLoadedTilesByKey.Keys);

            for (int i = 0; i < loadedFullKeys.Count; i++)
            {
                UnloadVisibleTile(loadedFullKeys[i]);
            }

            List<string> loadedThumbnailKeys = new List<string>(_thumbnailLoadedTilesByKey.Keys);

            for (int i = 0; i < loadedThumbnailKeys.Count; i++)
            {
                UnloadThumbnailTile(loadedThumbnailKeys[i]);
            }

            _loadedTileCenterZoneId = null;
            _loadedTileZ = int.MinValue;
        }

        private bool EnsureTileIndexForCurrentLayer(
            AutomapZoneCoord currentCoord,
            int displayZ,
            string source
        )
        {
            string automapDir = GetAutomapTileDirectory();

            if (!_tileIndexDirty &&
                _indexedTileDirectory == automapDir &&
                _indexedWorld == currentCoord.World &&
                _indexedZ == displayZ)
            {
                return true;
            }

            _knownTilesByKey.Clear();

            _indexedTileDirectory = automapDir;
            _indexedWorld = currentCoord.World;
            _indexedZ = displayZ;
            _tileIndexDirty = false;

            if (!Directory.Exists(automapDir))
            {
                SetCaptureStatus(source + ": automap tile directory does not exist: " + automapDir);
                return false;
            }

            string[] pngFiles = Directory.GetFiles(automapDir, "*.png");

            int indexedCount = 0;
            int skippedCount = 0;

            for (int i = 0; i < pngFiles.Length; i++)
            {
                string path = pngFiles[i];

                if (IsThumbnailPath(path))
                {
                    skippedCount++;
                    continue;
                }

                string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);

                AutomapZoneCoord tileCoord;

                if (!TryGetFullTileCoordFromPath(path, out tileCoord))
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

                AutomapKnownTile knownTile = new AutomapKnownTile();
                knownTile.Key = tileCoord.ZoneId;
                knownTile.FullPath = path;
                knownTile.ThumbnailPath = GetThumbnailPath(path);
                knownTile.Coord = tileCoord;

                _knownTilesByKey[knownTile.Key] = knownTile;
                indexedCount++;
            }

            SetCaptureStatus(
                source +
                ": indexed " +
                indexedCount +
                " tile(s) for " +
                currentCoord.World +
                " | " +
                GetLayerLabel(displayZ) +
                " | skipped=" +
                skippedCount
            );

            return true;
        }

        private void LoadCapturedZoneTilesForCurrentLayer(string source)
        {
            RefreshVisibleZoneTiles(source);
        }

        private void LoadAtlasLayerWithCurrentZonePriority(string source)
        {
            bool currentTileAlreadyExists = CurrentZoneTileExists();
            bool captureWasAlreadyPending = _capturePending;

            StartCaptureCurrentZoneImage(source);

            bool waitingForCurrentZoneCapture =
                !currentTileAlreadyExists &&
                !captureWasAlreadyPending &&
                _capturePending &&
                _captureLoadWhenComplete;

            if (waitingForCurrentZoneCapture)
            {
                ApplyMapPlaneTransform();

                SetCaptureStatus(
                    source +
                    ": waiting for current zone capture before loading atlas layer."
                );

                return;
            }

            LoadCapturedZoneTilesForCurrentLayer(source);
        }


        private void RefreshVisibleZoneTiles(string source)
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

                UpdateTileResolutionMode();
                ApplyTileLayerVisibility();

                if (_layerText != null)
                {
                    _layerText.text = GetFormattedLayerName(displayZ);
                }

                if (_loadedTileCenterZoneId != currentCoord.ZoneId ||
                    _loadedTileZ != displayZ)
                {
                    ClearLoadedZoneTiles();

                    _loadedTileCenterZoneId = currentCoord.ZoneId;
                    _loadedTileZ = displayZ;
                }

                if (!EnsureTileIndexForCurrentLayer(currentCoord, displayZ, source))
                {
                    ApplyMapPlaneTransform();
                    return;
                }

                ClearPendingVisibleTileLoads();

                int centerGlobalX;
                int centerGlobalY;

                GetViewportCenterGlobalZone(
                    currentCoord,
                    out centerGlobalX,
                    out centerGlobalY
                );

                if (_tileResolutionMode == TileResolutionMode.Thumbnail)
                {
                    List<AutomapKnownTile> missingThumbnailTiles =
                        new List<AutomapKnownTile>();

                    foreach (AutomapKnownTile knownTile in _knownTilesByKey.Values)
                    {
                        if (knownTile == null)
                        {
                            continue;
                        }

                        if (_thumbnailLoadedTilesByKey.ContainsKey(knownTile.Key))
                        {
                            continue;
                        }

                        if (string.IsNullOrEmpty(knownTile.ThumbnailPath) ||
                            !File.Exists(knownTile.ThumbnailPath))
                        {
                            continue;
                        }

                        missingThumbnailTiles.Add(knownTile);
                    }

                    missingThumbnailTiles.Sort(
                        delegate(AutomapKnownTile a, AutomapKnownTile b)
                        {
                            int aScore = GetTileLoadPriorityScore(
                                a,
                                currentCoord,
                                centerGlobalX,
                                centerGlobalY
                            );

                            int bScore = GetTileLoadPriorityScore(
                                b,
                                currentCoord,
                                centerGlobalX,
                                centerGlobalY
                            );

                            return aScore.CompareTo(bScore);
                        }
                    );

                    for (int i = 0; i < missingThumbnailTiles.Count; i++)
                    {
                        AutomapKnownTile knownTile = missingThumbnailTiles[i];

                        _pendingThumbnailTileLoads.Enqueue(knownTile);
                        _pendingThumbnailTileLoadKeys.Add(knownTile.Key);
                    }
                }
                else
                {
                    int minGlobalX;
                    int maxGlobalX;
                    int minGlobalY;
                    int maxGlobalY;

                    GetBufferedViewportGlobalZoneBounds(
                        currentCoord,
                        FullTileBufferZones,
                        out minGlobalX,
                        out maxGlobalX,
                        out minGlobalY,
                        out maxGlobalY
                    );

                    List<string> unloadFullKeys = new List<string>();

                    foreach (KeyValuePair<string, AutomapLoadedTile> pair in _visibleLoadedTilesByKey)
                    {
                        AutomapLoadedTile loadedTile = pair.Value;

                        if (loadedTile == null ||
                            loadedTile.KnownTile == null ||
                            !IsKnownTileInBounds(
                                loadedTile.KnownTile,
                                minGlobalX,
                                maxGlobalX,
                                minGlobalY,
                                maxGlobalY
                            ))
                        {
                            unloadFullKeys.Add(pair.Key);
                        }
                    }

                    for (int i = 0; i < unloadFullKeys.Count; i++)
                    {
                        UnloadVisibleTile(unloadFullKeys[i]);
                    }

                    List<AutomapKnownTile> missingFullTiles =
                        new List<AutomapKnownTile>();

                    foreach (AutomapKnownTile knownTile in _knownTilesByKey.Values)
                    {
                        if (knownTile == null)
                        {
                            continue;
                        }

                        if (!IsKnownTileInBounds(
                            knownTile,
                            minGlobalX,
                            maxGlobalX,
                            minGlobalY,
                            maxGlobalY
                        ))
                        {
                            continue;
                        }

                        if (_visibleLoadedTilesByKey.ContainsKey(knownTile.Key))
                        {
                            continue;
                        }

                        missingFullTiles.Add(knownTile);
                    }

                    missingFullTiles.Sort(
                        delegate(AutomapKnownTile a, AutomapKnownTile b)
                        {
                            int aScore = GetTileLoadPriorityScore(
                                a,
                                currentCoord,
                                centerGlobalX,
                                centerGlobalY
                            );

                            int bScore = GetTileLoadPriorityScore(
                                b,
                                currentCoord,
                                centerGlobalX,
                                centerGlobalY
                            );

                            return aScore.CompareTo(bScore);
                        }
                    );

                    for (int i = 0; i < missingFullTiles.Count; i++)
                    {
                        AutomapKnownTile knownTile = missingFullTiles[i];

                        _pendingVisibleTileLoads.Enqueue(knownTile);
                        _pendingVisibleTileLoadKeys.Add(knownTile.Key);
                    }
                }

                ApplyMapPlaneTransform();

                ProcessPendingVisibleTileLoads();

                SetCaptureStatus(
                    source +
                    ": mode=" +
                    _tileResolutionMode +
                    " full loaded=" +
                    _visibleLoadedTilesByKey.Count +
                    " thumb loaded=" +
                    _thumbnailLoadedTilesByKey.Count +
                    " indexed=" +
                    _knownTilesByKey.Count +
                    " full queued=" +
                    _pendingVisibleTileLoads.Count +
                    " thumb queued=" +
                    _pendingThumbnailTileLoads.Count +
                    " | " +
                    currentCoord.World +
                    " | " +
                    GetLayerLabel(displayZ) +
                    " | Center view: (" +
                    centerGlobalX +
                    ", " +
                    centerGlobalY +
                    ")"
                );
            }
            catch (Exception ex)
            {
                SetCaptureStatus(
                    source +
                    ": visible tile refresh failed: " +
                    ex.GetType().Name +
                    ": " +
                    ex.Message
                );
            }
        }
        

        private bool LoadVisibleTile(
            AutomapKnownTile knownTile,
            AutomapZoneCoord currentCoord
        )
        {
            if (knownTile == null)
            {
                return false;
            }

            return LoadTileRepresentation(
                knownTile,
                currentCoord,
                knownTile.FullPath,
                _fullTileContainer,
                _visibleLoadedTilesByKey,
                "FullZoneTile_"
            );
        }

        private bool LoadThumbnailTile(
            AutomapKnownTile knownTile,
            AutomapZoneCoord currentCoord
        )
        {
            if (knownTile == null)
            {
                return false;
            }

            return LoadTileRepresentation(
                knownTile,
                currentCoord,
                knownTile.ThumbnailPath,
                _thumbnailTileContainer,
                _thumbnailLoadedTilesByKey,
                "ThumbZoneTile_"
            );
        }

        private bool LoadTileRepresentation(
            AutomapKnownTile knownTile,
            AutomapZoneCoord currentCoord,
            string path,
            RectTransform parent,
            Dictionary<string, AutomapLoadedTile> loadedTilesByKey,
            string objectPrefix
        )
        {
            if (knownTile == null)
            {
                return false;
            }

            if (parent == null)
            {
                return false;
            }

            if (loadedTilesByKey.ContainsKey(knownTile.Key))
            {
                return true;
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return false;
            }

            Texture2D texture = null;
            UnityEngine.GameObject tileObject = null;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);

                texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);

                if (!texture.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(texture);
                    return false;
                }

                texture.filterMode = UnityEngine.FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;

                tileObject = new UnityEngine.GameObject(
                    objectPrefix + knownTile.Key
                );

                tileObject.transform.SetParent(parent.transform, false);

                RectTransform tileRect = tileObject.AddComponent<RectTransform>();
                tileRect.anchorMin = new Vector2(0.5f, 0.5f);
                tileRect.anchorMax = new Vector2(0.5f, 0.5f);
                tileRect.pivot = new Vector2(0.5f, 0.5f);

                // Critical: both full and thumbnail textures occupy the same logical tile slot.
                tileRect.sizeDelta = new Vector2(
                    AutomapTilePixelWidth,
                    AutomapTilePixelHeight
                );

                int relativeZoneX = knownTile.Coord.GlobalZoneX - currentCoord.GlobalZoneX;
                int relativeZoneY = knownTile.Coord.GlobalZoneY - currentCoord.GlobalZoneY;

                tileRect.anchoredPosition = new Vector2(
                    relativeZoneX * AutomapTilePixelWidth,
                    -relativeZoneY * AutomapTilePixelHeight
                );

                RawImage rawImage = tileObject.AddComponent<RawImage>();
                rawImage.texture = texture;
                rawImage.color = Color.white;
                rawImage.raycastTarget = false;

                AutomapLoadedTile loadedTile = new AutomapLoadedTile();
                loadedTile.KnownTile = knownTile;
                loadedTile.Texture = texture;
                loadedTile.GameObject = tileObject;

                loadedTilesByKey[knownTile.Key] = loadedTile;

                return true;
            }
            catch
            {
                if (tileObject != null)
                {
                    UnityEngine.Object.Destroy(tileObject);
                }

                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }

                return false;
            }
        }

        private void UnloadVisibleTile(string key)
        {
            UnloadLoadedTile(key, _visibleLoadedTilesByKey);
        }

        private void UnloadThumbnailTile(string key)
        {
            UnloadLoadedTile(key, _thumbnailLoadedTilesByKey);
        }

        private void UnloadLoadedTile(
            string key,
            Dictionary<string, AutomapLoadedTile> loadedTilesByKey
        )
        {
            AutomapLoadedTile loadedTile;

            if (!loadedTilesByKey.TryGetValue(key, out loadedTile))
            {
                return;
            }

            if (loadedTile != null)
            {
                if (loadedTile.GameObject != null)
                {
                    UnityEngine.Object.Destroy(loadedTile.GameObject);
                }

                if (loadedTile.Texture != null)
                {
                    UnityEngine.Object.Destroy(loadedTile.Texture);
                }
            }

            loadedTilesByKey.Remove(key);
        }

        private void ClearPendingVisibleTileLoads()
        {
            _pendingVisibleTileLoads.Clear();
            _pendingVisibleTileLoadKeys.Clear();

            _pendingThumbnailTileLoads.Clear();
            _pendingThumbnailTileLoadKeys.Clear();
        }

        private void ProcessPendingVisibleTileLoads()
        {
            if (_pendingVisibleTileLoads.Count == 0 &&
                _pendingThumbnailTileLoads.Count == 0)
            {
                return;
            }

            if (!_isOpen)
            {
                ClearPendingVisibleTileLoads();
                return;
            }

            Zone currentZone = The.Player?.GetCurrentZone();

            if (currentZone == null)
            {
                ClearPendingVisibleTileLoads();
                return;
            }

            AutomapZoneCoord currentCoord;

            if (!AutomapZoneCoord.TryParse(currentZone.ZoneID, out currentCoord))
            {
                ClearPendingVisibleTileLoads();
                return;
            }

            EnsureDisplayZInitialized();

            int loadedFullThisFrame = 0;
            int loadedThumbThisFrame = 0;

            if (_tileResolutionMode == TileResolutionMode.Full)
            {
                while (_pendingVisibleTileLoads.Count > 0 &&
                    loadedFullThisFrame < MaxTileLoadsPerFrame)
                {
                    AutomapKnownTile knownTile = _pendingVisibleTileLoads.Dequeue();

                    if (knownTile == null)
                    {
                        continue;
                    }

                    _pendingVisibleTileLoadKeys.Remove(knownTile.Key);

                    if (_visibleLoadedTilesByKey.ContainsKey(knownTile.Key))
                    {
                        continue;
                    }

                    if (knownTile.Coord.World != currentCoord.World)
                    {
                        continue;
                    }

                    if (knownTile.Coord.Z != _displayZ)
                    {
                        continue;
                    }

                    if (LoadVisibleTile(knownTile, currentCoord))
                    {
                        loadedFullThisFrame++;
                    }
                }
            }
            else
            {
                _pendingVisibleTileLoads.Clear();
                _pendingVisibleTileLoadKeys.Clear();
            }

            while (_pendingThumbnailTileLoads.Count > 0 &&
                loadedThumbThisFrame < MaxThumbnailTileLoadsPerFrame)
            {
                AutomapKnownTile knownTile = _pendingThumbnailTileLoads.Dequeue();

                if (knownTile == null)
                {
                    continue;
                }

                _pendingThumbnailTileLoadKeys.Remove(knownTile.Key);

                if (_thumbnailLoadedTilesByKey.ContainsKey(knownTile.Key))
                {
                    continue;
                }

                if (knownTile.Coord.World != currentCoord.World)
                {
                    continue;
                }

                if (knownTile.Coord.Z != _displayZ)
                {
                    continue;
                }

                if (LoadThumbnailTile(knownTile, currentCoord))
                {
                    loadedThumbThisFrame++;
                }
            }

            if (loadedFullThisFrame > 0 || loadedThumbThisFrame > 0)
            {
                ApplyMapPlaneTransform();

                SetCaptureStatus(
                    "Loading atlas tiles: full +" +
                    loadedFullThisFrame +
                    ", thumb +" +
                    loadedThumbThisFrame +
                    " | full loaded " +
                    _visibleLoadedTilesByKey.Count +
                    ", thumb loaded " +
                    _thumbnailLoadedTilesByKey.Count +
                    " | full queued " +
                    _pendingVisibleTileLoads.Count +
                    ", thumb queued " +
                    _pendingThumbnailTileLoads.Count +
                    " | " +
                    GetLayerLabel(_displayZ)
                );
            }
        }



        private int GetTileDistanceScore(
            AutomapKnownTile knownTile,
            int centerGlobalX,
            int centerGlobalY
        )
        {
            if (knownTile == null)
            {
                return int.MaxValue;
            }

            int dx = knownTile.Coord.GlobalZoneX - centerGlobalX;
            int dy = knownTile.Coord.GlobalZoneY - centerGlobalY;

            return dx * dx + dy * dy;
        }

        private void GetViewportCenterGlobalZone(
            AutomapZoneCoord currentCoord,
            out int centerGlobalX,
            out int centerGlobalY
        )
        {
            float zoom = _zoom;

            if (zoom <= 0f)
            {
                zoom = 1f;
            }

            // Map-space pixel coordinate currently under the center of the viewport.
            // The viewport center is local point zero; mapPlaneOffset moves the map under it.
            float centerMapPixelX = -_mapPlaneOffset.x / zoom;
            float centerMapPixelY = -_mapPlaneOffset.y / zoom;

            int relativeZoneX =
                Mathf.RoundToInt(centerMapPixelX / AutomapTilePixelWidth);

            // Tile placement negates world Y:
            // tile local Y = -relativeZoneY * tileHeight.
            int relativeZoneY =
                Mathf.RoundToInt(-centerMapPixelY / AutomapTilePixelHeight);

            centerGlobalX = currentCoord.GlobalZoneX + relativeZoneX;
            centerGlobalY = currentCoord.GlobalZoneY + relativeZoneY;
        }

        private int GetTileLoadPriorityScore(
            AutomapKnownTile knownTile,
            AutomapZoneCoord currentCoord,
            int centerGlobalX,
            int centerGlobalY
        )
        {
            if (knownTile == null)
            {
                return int.MaxValue;
            }

            if (knownTile.Key == currentCoord.ZoneId)
            {
                return 0;
            }

            return 1 + GetTileDistanceScore(
                knownTile,
                centerGlobalX,
                centerGlobalY
            );
        }

        private void GetBufferedViewportGlobalZoneBounds(
            AutomapZoneCoord currentCoord,
            int bufferZones,
            out int minGlobalX,
            out int maxGlobalX,
            out int minGlobalY,
            out int maxGlobalY
        )
        {
            int centerGlobalX;
            int centerGlobalY;

            GetViewportCenterGlobalZone(
                currentCoord,
                out centerGlobalX,
                out centerGlobalY
            );

            float zoom = _zoom;

            if (zoom <= 0f)
            {
                zoom = 1f;
            }

            float viewportWidth = 1920f;
            float viewportHeight = 1080f;

            if (_mapViewportRect != null)
            {
                viewportWidth = Mathf.Max(1f, _mapViewportRect.rect.width);
                viewportHeight = Mathf.Max(1f, _mapViewportRect.rect.height);
            }

            float mapPixelWidth = viewportWidth / zoom;
            float mapPixelHeight = viewportHeight / zoom;

            int radiusX =
                Mathf.CeilToInt(mapPixelWidth / (AutomapTilePixelWidth * 2f)) +
                bufferZones;

            int radiusY =
                Mathf.CeilToInt(mapPixelHeight / (AutomapTilePixelHeight * 2f)) +
                bufferZones;

            minGlobalX = centerGlobalX - radiusX;
            maxGlobalX = centerGlobalX + radiusX;
            minGlobalY = centerGlobalY - radiusY;
            maxGlobalY = centerGlobalY + radiusY;
        }

        private bool IsKnownTileInBounds(
            AutomapKnownTile knownTile,
            int minGlobalX,
            int maxGlobalX,
            int minGlobalY,
            int maxGlobalY
        )
        {
            if (knownTile == null)
            {
                return false;
            }

            int x = knownTile.Coord.GlobalZoneX;
            int y = knownTile.Coord.GlobalZoneY;

            return
                x >= minGlobalX &&
                x <= maxGlobalX &&
                y >= minGlobalY &&
                y <= maxGlobalY;
        }



        private void UpdateTileResolutionMode()
        {
            if (_tileResolutionMode == TileResolutionMode.Thumbnail)
            {
                if (_zoom > ThumbnailModeExitZoom)
                {
                    _tileResolutionMode = TileResolutionMode.Full;
                }

                return;
            }

            if (_zoom < ThumbnailModeEnterZoom)
            {
                _tileResolutionMode = TileResolutionMode.Thumbnail;
            }
        }

        private void ApplyTileLayerVisibility()
        {
            if (_thumbnailTileContainer != null)
            {
                // Keep thumbnails visible as an underlay when available.
                _thumbnailTileContainer.gameObject.SetActive(true);
            }

            if (_fullTileContainer != null)
            {
                _fullTileContainer.gameObject.SetActive(
                    _tileResolutionMode == TileResolutionMode.Full
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