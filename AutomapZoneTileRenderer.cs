using System;
using System.IO;
using ConsoleLib.Console;
using UnityEngine;
using XRL;
using XRL.UI;
using XRL.World;
using XRL.World.Capabilities;
using Kobold;

namespace CoQAutoMap
{
    public sealed partial class AutomapController
    {
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
    }
}