using System;
using UnityEngine;
using XRL;
using XRL.UI;
using XRL.World;

namespace CoQAutoMap
{
    public sealed partial class AutomapController
    {
         private void HandleRawAutomapControls()
        {
            try
            {
                // Keep Qud's queued input empty while the modal overlay is active.
                ControlManager.ConsumeAllInput();

                if (Input.GetKeyDown(UnityEngine.KeyCode.W))
                {
                    ToggleWorldMapOverlay();
                    return;
                }

                if (_worldMapVisible)
                {
                    if (Input.GetKeyDown(UnityEngine.KeyCode.Escape))
                    {
                        ToggleWorldMapOverlay();
                        return;
                    }

                    // While the world map overlay is visible, do not let pan/zoom/layer
                    // controls affect the automap underneath.
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.Escape))
                {
                    AutomapInputGate.BlockUntilEscapeReleased();
                    CloseWindow("Raw Esc");
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.LeftArrow) ||
                    Input.GetKeyDown(UnityEngine.KeyCode.Keypad4))
                {
                    PanWest();
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.RightArrow) ||
                    Input.GetKeyDown(UnityEngine.KeyCode.Keypad6))
                {
                    PanEast();
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.UpArrow) ||
                    Input.GetKeyDown(UnityEngine.KeyCode.Keypad8))
                {
                    PanNorth();
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.DownArrow) ||
                    Input.GetKeyDown(UnityEngine.KeyCode.Keypad2))
                {
                    PanSouth();
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.PageUp))
                {
                    LayerUp();
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.PageDown))
                {
                    LayerDown();
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.Equals) ||
                    Input.GetKeyDown(UnityEngine.KeyCode.KeypadPlus))
                {
                    ZoomIn();
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.Minus) ||
                    Input.GetKeyDown(UnityEngine.KeyCode.KeypadMinus))
                {
                    ZoomOut();
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.Home) ||
                    Input.GetKeyDown(UnityEngine.KeyCode.Keypad5))
                {
                    ResetView();
                    return;
                }

            }
            catch (Exception ex)
            {
                SetCaptureStatus(
                    "Automap input failed: " +
                    ex.GetType().Name +
                    ": " +
                    ex.Message
                );
            }
        }
         private bool TryGetCurrentZoneCoord(out AutomapZoneCoord coord)
        {
            coord = default(AutomapZoneCoord);

            Zone currentZone = The.Player?.GetCurrentZone();

            if (currentZone == null)
            {
                return false;
            }

            return AutomapZoneCoord.TryParse(currentZone.ZoneID, out coord);
        }

        private int GetCurrentZoneZOrSurface()
        {
            AutomapZoneCoord currentCoord;

            if (TryGetCurrentZoneCoord(out currentCoord))
            {
                return currentCoord.Z;
            }

            return 10;
        }

        private void EnsureDisplayZInitialized()
        {
            if (_displayZ != int.MinValue)
            {
                return;
            }

            _displayZ = GetCurrentZoneZOrSurface();
        }

        private string GetLayerLabel(int z)
        {
            // Temporary but player-readable enough.
            // Later we can tune names.
            if (z == 10)
            {
                return "Surface / Z10";
            }

            if (z > 10)
            {
                return "Subterranean " + (z - 10) + " / Z" + z;
            }

            return "Aboveground " + (10 - z) + " / Z" + z;
        }

        private void PanNorth()
        {
            _panY--;
            _mapPlaneOffset.y -= PanStep;
            RefreshMapTiles("PanNorth");
        }

        private void PanSouth()
        {
            _panY++;
            _mapPlaneOffset.y += PanStep;
            RefreshMapTiles("PanSouth");
        }

        private void PanWest()
        {
            _panX--;
            _mapPlaneOffset.x += PanStep;
            RefreshMapTiles("PanWest");
        }

        private void PanEast()
        {
            _panX++;
            _mapPlaneOffset.x -= PanStep;
            RefreshMapTiles("PanEast");
        }

        private void LayerUp()
        {
            EnsureDisplayZInitialized();

            // In Qud, smaller Z is higher/aboveground.
            _displayZ--;

            LoadCapturedZoneTilesForCurrentLayer("LayerUp");
        }

        private void LayerDown()
        {
            EnsureDisplayZInitialized();

            // Larger Z is deeper/subterranean.
            _displayZ++;

            LoadCapturedZoneTilesForCurrentLayer("LayerDown");
        }

        private void ZoomIn()
        {
            SetZoomAroundViewportCenter(
                Mathf.Min(MaxZoom, _zoom * ZoomInFactor),
                "ZoomIn"
            );
        }

        private void ZoomOut()
        {
            SetZoomAroundViewportCenter(
                Mathf.Max(MinZoom, _zoom * ZoomOutFactor),
                "ZoomOut"
            );
        }

        private void SetZoomAroundViewportCenter(float newZoom, string source)
        {
            float oldZoom = _zoom;

            if (oldZoom <= 0f)
            {
                oldZoom = 1f;
            }

            if (Mathf.Approximately(oldZoom, newZoom))
            {
                _zoom = newZoom;
                RefreshMapTiles(source);
                return;
            }

            float ratio = newZoom / oldZoom;

            // Preserve the map coordinate currently under the viewport center.
            _mapPlaneOffset *= ratio;

            _zoom = newZoom;

            RefreshMapTiles(source);
        }

        private void ResetView()
        {
            _panX = 0;
            _panY = 0;
            _mapPlaneOffset = Vector2.zero;
            _zoom = 1.0f;
            _displayZ = GetCurrentZoneZOrSurface();

            LoadCapturedZoneTilesForCurrentLayer("ResetView");
        }
    }
}