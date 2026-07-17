using System;
using System.Collections.Generic;
using System.IO;
//using ConsoleLib.Console;
using UnityEngine;
using UnityEngine.UI;
using XRL;
using XRL.UI;
using XRL.World;
//using System.Reflection;
//using HarmonyLib;
//using Genkit;
//using Kobold;
//using XRL.World.Capabilities;

using NavigationContext = XRL.UI.Framework.NavigationContext;
using NavigationController = XRL.UI.Framework.NavigationController;
using FrameworkEvent = XRL.UI.Framework.Event;

namespace CoQAutoMap
{
    public sealed partial class AutomapController : MonoBehaviour
    {
        private const string ControllerName = "CoQAutoMap_Controller";
        private const string CanvasName = "CoQAutoMap_Canvas";
        private const string LogFileName = "CoQAutoMap.txt";

        private const float PanStep = 80f;

        // Multiplicative zoom feels better than additive zoom.
        // Zoom-out is intentionally stronger because finding offscreen tiles matters.
        private const float ZoomInFactor = 1.15f;
        private const float ZoomOutFactor = 0.82f;

        private const float MinZoom = 0.04f;
        private const float MaxZoom = 1.50f;

        private static AutomapController _instance;

        private Canvas _canvas;
        private UnityEngine.GameObject _root;

        private UnityEngine.UI.Text _titleText;
        private UnityEngine.UI.Text _layerText;
        private UnityEngine.UI.Text _statusText;
        private UnityEngine.UI.Text _helpText;

        private UnityEngine.GameObject _worldMapRoot;
        private RectTransform _worldMapPlane;
        private RawImage _worldMapImage;
        private Texture2D _worldMapTexture;
        private bool _worldMapVisible;
        private RectTransform _worldMapTargetMarker;


        private bool _isOpen;
        private bool _suppressToggleUntilReleased;

        private string _previousGameView;
        private NavigationContext _previousNavigationContext;
        private NavigationContext _automapNavigationContext;
      
        private int _panX;
        private int _panY;
        // Absolute Qud Z layer currently displayed.
        // Surface is normally Z10.
        private int _displayZ = int.MinValue;
        private float _zoom = 1.0f;

        private Vector2 _mapPlaneOffset = Vector2.zero;

        private RectTransform _mapPlane;

        private RectTransform _zoneTileContainer;
        private readonly List<UnityEngine.GameObject> _loadedZoneTileObjects = new List<UnityEngine.GameObject>();
        private readonly List<Texture2D> _loadedZoneTileTextures = new List<Texture2D>();

        private bool _capturePending;
        private bool _captureComplete;
        private bool _captureLoadWhenComplete;

        private string _capturePath;
        private string _captureError;
        private DateTime _captureStartTime;

        public static void QueueDeactivatedZoneCapture(Zone zone, string source)
        {
            try
            {
                if (_instance == null)
                {

                    return;
                }

                _instance.StartCaptureZoneImage(
                    zone,
                    source + " auto-capture",
                    loadWhenComplete: false
                );
            }
            catch
            {
            }
        }

        public static void DebugLog(string message)
        {
            // Debug logging disabled for normal use.
            // Uncomment while investigating input/capture/system issues.
            // Log(message);
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
                catch
                {
                }

                //Popup.Show("CoQ Auto-Map NavigationContext loaded.\n\nPress Ctrl+M to open the Automap window.");
            }
            catch (Exception ex)
            {
                Popup.Show("CoQ Auto-Map install exception:\n\n" + ex.GetType().Name + "\n" + ex.Message);
            }
        }

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
            catch
            {
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
            catch
            {
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

        private static void Log(string message)
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    LogFileName
                );

                File.AppendAllText(
                    path,
                    DateTime.UtcNow.ToString("u") + " | " + message + "\n"
                );
            }
            catch
            {
            }
        }
    }
}