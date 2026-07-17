using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using XRL;
using XRL.UI;
using XRL.World;

using NavigationContext = XRL.UI.Framework.NavigationContext;
using NavigationController = XRL.UI.Framework.NavigationController;

namespace CoQAutoMap
{
    public sealed partial class AutomapController
    {

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
                    _layerText.text = "Layer: " + GetLayerLabel(displayZ);
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
                SetCaptureStatus(source + ": LoadCapturedZoneTilesForCurrentLayer exception: " + ex.GetType().Name);
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
                _layerText.text = "Layer: " + GetLayerLabel(_displayZ);
            }

            string currentContext = "<no NavigationController>";

            if (NavigationController.instance != null)
            {
                currentContext = SafeContext(NavigationController.instance.activeContext);
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

        private static string SafeContext(NavigationContext context)
        {
            if (context == null)
            {
                return "<null>";
            }

            try
            {
                return context.ToString();
            }
            catch
            {
                return "<context ToString failed>";
            }
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