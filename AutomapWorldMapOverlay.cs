using System;
using ConsoleLib.Console;
using UnityEngine;
using XRL;
using XRL.World;
using XRL.World.Capabilities;
using Genkit;
using Kobold;

namespace CoQAutoMap
{
    public sealed partial class AutomapController
    {
         private void ToggleWorldMapOverlay()
        {
            EnsureUiCreated();

            _worldMapVisible = !_worldMapVisible;

            if (_worldMapRoot != null)
            {
                _worldMapRoot.SetActive(_worldMapVisible);
            }

            if (_worldMapVisible)
            {
                RenderWorldMapOverlay("ToggleWorldMapOverlay");
            }
            else
            {
                RefreshMapTiles("ToggleWorldMapOverlay off");
            }
        }

        private bool TryGetCurrentWorldMapLocation(out Location2D location)
        {
            location = null;

            AutomapZoneCoord coord;

            if (!TryGetCurrentZoneCoord(out coord))
            {
                return false;
            }

            location = Location2D.Get(coord.ParasangX, coord.ParasangY);
            return true;
        }

        private float GetWorldMapHighlightDistanceSquared(
            Location2D highlight,
            int x,
            int y,
            int px,
            int py,
            int tileWidth,
            int tileHeight
        )
        {
            if (highlight == null)
            {
                return float.MaxValue;
            }

            float highlightX = highlight.X * tileWidth + tileWidth / 2f;
            float highlightY = highlight.Y * tileHeight + tileHeight / 4f;

            int pixelX = x * tileWidth + px;
            int pixelY = y * (tileHeight + 1) - py;

            float dx = highlightX - pixelX;
            float dy = highlightY - pixelY;

            return dx * dx + dy * dy;
        }

        private void CenterWorldMapOn(int x, int y)
        {
            if (_worldMapPlane == null || _worldMapRoot == null)
            {
                return;
            }

            RectTransform rootRect = _worldMapRoot.transform as RectTransform;

            if (rootRect == null)
            {
                return;
            }

            Vector2 size = rootRect.rect.size;

            _worldMapPlane.anchoredPosition = new Vector2(
                -((x * 32f + 16f) - size.x / 2f),
                (y * 48f + 24f) - size.y / 2f
            );
        }

        private void RenderWorldMapOverlay(string source)
        {
            try
            {
                EnsureUiCreated();

                if (_worldMapImage == null || _worldMapPlane == null)
                {
                    SetCaptureStatus(source + ": world map UI was not created.");
                    return;
                }

                Location2D playerLocation;

                if (!TryGetCurrentWorldMapLocation(out playerLocation))
                {
                    SetCaptureStatus(source + ": could not get current world map location.");
                    return;
                }

                int width = 1280;
                int height = 600;
                int tileWidth = 16;
                int tileHeight = 24;

                if (_worldMapTexture == null)
                {
                    _worldMapTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    _worldMapTexture.filterMode = UnityEngine.FilterMode.Bilinear;
                    _worldMapTexture.wrapMode = TextureWrapMode.Clamp;

                    _worldMapPlane.sizeDelta = new Vector2(width, height);
                }

                _worldMapImage.texture = _worldMapTexture;
                _worldMapImage.color = Color.white;
                _worldMapImage.gameObject.SetActive(true);

                Zone zone = The.Game.ZoneManager.GetZone("JoppaWorld");

                if (zone == null)
                {
                    SetCaptureStatus(source + ": JoppaWorld zone was null.");
                    return;
                }

                UnityEngine.Color baseColor =
                    ConsoleLib.Console.ColorUtility.FromWebColor("041312");

                UnityEngine.Color highlightColor =
                    ConsoleLib.Console.ColorUtility.ColorMap['K'];

                UnityEngine.Color[] pixels = _worldMapTexture.GetPixels();

                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i] = baseColor;
                }

                using (RenderEvent E = RenderEvent.Pool.Get())
                {
                    for (int x = 0; x < 80; x++)
                    {
                        for (int y = 0; y < 25; y++)
                        {
                            zone.GetCell(x, y).Render(E);

                            Sprite sprite = SpriteManager.GetUnitySprite(E.GetSpriteName());

                            if (sprite == null || sprite.texture == null)
                            {
                                continue;
                            }

                            UnityEngine.Color foregroundColor = E.GetForegroundColor();
                            UnityEngine.Color detailColor = E.GetDetailColor();

                            for (int px = 0; px < tileWidth; px++)
                            {
                                for (int py = 0; py < tileHeight; py++)
                                {
                                    UnityEngine.Color spritePixel =
                                        sprite.texture.GetPixel(px, py);

                                    UnityEngine.Color pixelColor = baseColor;

                                    float distanceSquared = GetWorldMapHighlightDistanceSquared(
                                        playerLocation,
                                        x,
                                        y,
                                        px,
                                        py,
                                        tileWidth,
                                        tileHeight
                                    );

                                    float t = distanceSquared >= 2400.0f
                                        ? 1f
                                        : Mathf.Min(
                                            1f,
                                            Mathf.Max(0.0f, Mathf.Sqrt(distanceSquared) / 48f)
                                        );

                                    if (spritePixel.a <= 0f)
                                    {
                                        pixelColor = baseColor;
                                    }
                                    else if (spritePixel.r < 0.5f)
                                    {
                                        pixelColor = Color.Lerp(foregroundColor, highlightColor, t);
                                    }
                                    else if (spritePixel.r > 0.5f)
                                    {
                                        pixelColor = Color.Lerp(detailColor, highlightColor, t);
                                    }

                                    int imageX = x * tileWidth + px;
                                    int imageY = (24 - y) * tileHeight + py;

                                    pixels[imageX + imageY * width] = pixelColor;
                                }
                            }
                        }
                    }
                }

                _worldMapTexture.SetPixels(pixels);
                _worldMapTexture.Apply(updateMipmaps: false);

                //CenterWorldMapOn(playerLocation.X, playerLocation.Y);
                _worldMapPlane.anchoredPosition = Vector2.zero;
                _worldMapPlane.localScale = Vector3.one;

                PositionWorldMapTargetMarker(playerLocation.X, playerLocation.Y);

                SetCaptureStatus(
                    source +
                    ": world map overlay highlighting parasang (" +
                    playerLocation.X +
                    ", " +
                    playerLocation.Y +
                    ")"
                );
            }
            catch (Exception ex)
            {
                SetCaptureStatus(source + ": RenderWorldMapOverlay exception: " + ex.GetType().Name);
            }
        }

        private void PositionWorldMapTargetMarker(int parasangX, int parasangY)
        {
            if (_worldMapTargetMarker == null)
            {
                return;
            }

            // Our RawImage texture is 1280 x 600.
            // The world map is 80 x 25 parasangs.
            // Each parasang cell is 16 x 24 pixels.
            float cellWidth = 16f;
            float cellHeight = 24f;

            float mapWidth = 1280f;
            float mapHeight = 600f;

            float markerX =
                -mapWidth / 2f +
                parasangX * cellWidth +
                cellWidth / 2f;

            float markerY =
                mapHeight / 2f -
                parasangY * cellHeight -
                cellHeight / 2f;

            _worldMapTargetMarker.anchoredPosition = new Vector2(markerX, markerY);
            _worldMapTargetMarker.gameObject.SetActive(true);
        }
    }
}