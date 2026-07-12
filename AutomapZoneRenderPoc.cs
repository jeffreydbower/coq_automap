using System;
using System.Collections.Generic;
using System.IO;
using ConsoleLib.Console;
using UnityEngine;
using UnityEngine.UI;
using XRL;
using XRL.UI;
using XRL.World;
using System.Reflection;
using HarmonyLib;
using Genkit;
using Kobold;
using XRL.World.Capabilities;

using NavigationContext = XRL.UI.Framework.NavigationContext;
using NavigationController = XRL.UI.Framework.NavigationController;
using FrameworkEvent = XRL.UI.Framework.Event;

namespace CoQAutoMap
{
    [PlayerMutator]
    [HasCallAfterGameLoaded]
    public sealed class AutomapWindowPocBootstrap : IPlayerMutator
    {
        public void mutate(XRL.World.GameObject player)
        {
            AutomapWindowPocController.EnsureInstalled("PlayerMutator.mutate");
        }

        [CallAfterGameLoaded]
        public static void AfterGameLoaded()
        {
            AutomapWindowPocController.EnsureInstalled("CallAfterGameLoaded");
        }
    }

    public sealed class AutomapWindowPocController : MonoBehaviour
    {
        private const string ControllerName = "CoQAutoMap_WindowPoc_Controller";
        private const string CanvasName = "CoQAutoMap_WindowPoc_Canvas";
        private const string LogFileName = "CoQAutoMap_WindowPoc.txt";

        private const int VisibleColumns = 30;
        private const int VisibleRows = 10;

        private const float PanStep = 80f;

        // Multiplicative zoom feels better than additive zoom.
        // Zoom-out is intentionally stronger because finding offscreen tiles matters.
        private const float ZoomInFactor = 1.15f;
        private const float ZoomOutFactor = 0.82f;

        private const float MinZoom = 0.04f;
        private const float MaxZoom = 1.50f;

        private static AutomapWindowPocController _instance;

        private Canvas _canvas;
        private UnityEngine.GameObject _root;
        private RectTransform _mapContent;
        private GridLayoutGroup _grid;
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




        private readonly List<Image> _tiles = new List<Image>();

        private bool _isOpen;
        private bool _suppressToggleUntilReleased;

        private string _previousGameView;
        private NavigationContext _previousNavigationContext;
        private NavigationContext _automapNavigationContext;

        private int _layerZ;        
        private int _panX;
        private int _panY;
        // Absolute Qud Z layer currently displayed.
        // Surface is normally Z10.
        private int _displayZ = int.MinValue;
        private float _zoom = 1.0f;

        private Vector2 _mapPlaneOffset = Vector2.zero;

        private RawImage _renderedZoneImage;
        private RectTransform _renderedZoneImageRect;
        private RectTransform _mapPlane;

        private RectTransform _zoneTileContainer;
        private readonly List<UnityEngine.GameObject> _loadedZoneTileObjects = new List<UnityEngine.GameObject>();
        private readonly List<Texture2D> _loadedZoneTileTextures = new List<Texture2D>();

        private Texture2D _loadedZoneTexture;

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
                    Log(
                        source +
                        ": QueueDeactivatedZoneCapture skipped; controller instance is null. zone=" +
                        (zone != null ? zone.ZoneID : "<null>")
                    );

                    return;
                }

                _instance.StartCaptureZoneImage(
                    zone,
                    source + " auto-capture",
                    loadWhenComplete: false
                );
            }
            catch (Exception ex)
            {
                Log(source + ": QueueDeactivatedZoneCapture EXCEPTION: " + ex);
            }
        }

        public static void DebugLog(string message)
        {
            Log(message);
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
                    Log(source + ": controller already installed.");
                    return;
                }

                UnityEngine.GameObject existing = UnityEngine.GameObject.Find(ControllerName);

                if (existing != null)
                {
                    _instance = existing.GetComponent<AutomapWindowPocController>();

                    if (_instance != null)
                    {
                        Log(source + ": found existing controller.");
                        return;
                    }
                }

                UnityEngine.GameObject controllerObject = new UnityEngine.GameObject(ControllerName);
                DontDestroyOnLoad(controllerObject);

               _instance = controllerObject.AddComponent<AutomapWindowPocController>();
               

                AutomapInputGatePoc.Install();

                try
                {
                    The.Game?.RequireSystem<AutomapZoneCaptureSystem>();
                    Log(source + ": required AutomapZoneCaptureSystem.");
                }
                catch (Exception ex)
                {
                    Log(source + ": RequireSystem<AutomapZoneCaptureSystem> EXCEPTION: " + ex);
                }

                Log(source + ": installed Automap NavigationContext POC controller.");
                Popup.Show("CoQ Auto-Map NavigationContext POC loaded.\n\nPress Ctrl+M to open the Automap window.");
            }
            catch (Exception ex)
            {
                Log(source + ": EXCEPTION installing controller: " + ex);
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

                if (Input.GetKeyDown(UnityEngine.KeyCode.R))
                {
                    StartCaptureCurrentZoneImage("Raw R re-render");
                    return;
                }

                if (Input.GetKeyDown(UnityEngine.KeyCode.Escape))
                {
                    AutomapInputGatePoc.BlockUntilEscapeReleased();
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
                Log("HandleRawAutomapControls: EXCEPTION: " + ex);
            }
        }

        private void Update()
        {
            try
            {
                AutomapInputGatePoc.UpdateReleaseBlocks();

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
                    Log("Update: released Ctrl+M; toggle active again.");
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
                Log("Update: EXCEPTION: " + ex);
            }
        }

        private void OpenWindow(string source)
        {
            EnsureUiCreated();

            if (_root == null)
            {
                Log(source + ": failed to open; root was null.");
                return;
            }

            if (_isOpen)
            {
                Log(source + ": OpenWindow skipped; already open.");
                return;
            }

            _previousGameView = null;
            _previousNavigationContext = null;

            try
            {
                if (GameManager.Instance != null)
                {
                    _previousGameView = GameManager.Instance.CurrentGameView;
                }

                if (NavigationController.instance != null)
                {
                    _previousNavigationContext = NavigationController.instance.activeContext;
                }
            }
            catch (Exception ex)
            {
                Log(source + ": exception while capturing previous context/view: " + ex);
            }

            _root.SetActive(true);

            if (_canvas != null)
            {
                _canvas.sortingOrder = 5000;
                _canvas.enabled = true;
            }

            _isOpen = true;

            CreateAndActivateAutomapNavigationContext(source);
            ControlManager.ConsumeAllInput();
            ControlManager.ResetInput(false, false);

            _displayZ = GetCurrentZoneZOrSurface();

            RefreshMapTiles(source);

            StartCaptureCurrentZoneImage(source);

            Log(
                source +
                ": Automap window opened. previousGameView=" +
                Safe(_previousGameView) +
                " previousNav=" +
                SafeContext(_previousNavigationContext) +
                " currentNav=" +
                SafeContext(NavigationController.instance != null ? NavigationController.instance.activeContext : null) +
                "."
            );
        }

        private void CloseWindow(string source)
        {
            if (!_isOpen)
            {
                Log(source + ": CloseWindow skipped; already closed.");
                return;
            }

            _isOpen = false;

            if (_root != null)
            {
                _root.SetActive(false);
            }

            RestorePreviousNavigationContext(source);

            try
            {
                ControlManager.ConsumeAllInput();
                ControlManager.ResetInput(false, false);
            }
            catch (Exception ex)
            {
                Log(source + ": exception while consuming/resetting input: " + ex);
            }

            try
            {
                if (GameManager.Instance != null && _previousGameView != null)
                {
                    GameManager.EnsureGameView(_previousGameView);
                }
            }
            catch (Exception ex)
            {
                Log(source + ": exception while restoring game view: " + ex);
            }

            Log(
                source +
                ": Automap window closed. restoredGameView=" +
                Safe(_previousGameView) +
                " currentView=" +
                Safe(GameManager.Instance != null ? GameManager.Instance.CurrentGameView : null) +
                " currentNav=" +
                SafeContext(NavigationController.instance != null ? NavigationController.instance.activeContext : null) +
                "."
            );

            _automapNavigationContext = null;
            _previousNavigationContext = null;
            _previousGameView = null;
        }

        private void CreateAndActivateAutomapNavigationContext(string source)
        {
            try
            {
                _automapNavigationContext = new NavigationContext("CoQ Auto-Map");

                _automapNavigationContext.commandHandlers = new Dictionary<string, Action>();

                RegisterCommand("Cancel", () => CloseWindow("NavigationContext: Cancel"));
                RegisterCommand("CmdCancel", () => CloseWindow("NavigationContext: CmdCancel"));

                RegisterCommand("Navigate Up", PanNorth);
                RegisterCommand("Navigate Down", PanSouth);
                RegisterCommand("Navigate Left", PanWest);
                RegisterCommand("Navigate Right", PanEast);

                RegisterCommand("CmdMoveN", PanNorth);
                RegisterCommand("CmdMoveS", PanSouth);
                RegisterCommand("CmdMoveW", PanWest);
                RegisterCommand("CmdMoveE", PanEast);

                RegisterCommand("Page Up", LayerUp);
                RegisterCommand("Page Down", LayerDown);
                RegisterCommand("CmdPageUp", LayerUp);
                RegisterCommand("CmdPageDown", LayerDown);

                RegisterCommand("ZoomIn", ZoomIn);
                RegisterCommand("ZoomOut", ZoomOut);
                RegisterCommand("CmdZoomIn", ZoomIn);
                RegisterCommand("CmdZoomOut", ZoomOut);

                RegisterCommand("Accept", ResetView);
                RegisterCommand("CmdAccept", ResetView);

                _automapNavigationContext.ActivateAndEnable();

                Log(source + ": activated Automap NavigationContext.");
            }
            catch (Exception ex)
            {
                Log(source + ": EXCEPTION creating/activating Automap NavigationContext: " + ex);
                Popup.Show("CoQ Auto-Map NavigationContext exception:\n\n" + ex.GetType().Name + "\n" + ex.Message);
            }
        }

        private void RegisterCommand(string commandId, Action action)
        {
            if (_automapNavigationContext == null)
            {
                return;
            }

            if (_automapNavigationContext.commandHandlers == null)
            {
                _automapNavigationContext.commandHandlers = new Dictionary<string, Action>();
            }

            _automapNavigationContext.commandHandlers[commandId] =
                FrameworkEvent.Helpers.Handle(
                    () =>
                    {
                        try
                        {
                            action();
                            Log("NavigationContext handled command: " + commandId);
                        }
                        catch (Exception ex)
                        {
                            Log("NavigationContext command exception for " + commandId + ": " + ex);
                        }
                    }
                );
        }

        private void RestorePreviousNavigationContext(string source)
        {
            try
            {
                NavigationContext active =
                    NavigationController.instance != null
                        ? NavigationController.instance.activeContext
                        : null;

                if (_previousNavigationContext != null)
                {
                    _previousNavigationContext.ActivateAndEnable();
                    Log(source + ": restored previous NavigationContext: " + SafeContext(_previousNavigationContext));
                    return;
                }

                if (active == _automapNavigationContext && NavigationController.instance != null)
                {
                    NavigationController.instance.activeContext = null;
                    Log(source + ": cleared Automap NavigationContext; no previous context.");
                }
            }
            catch (Exception ex)
            {
                Log(source + ": EXCEPTION restoring previous NavigationContext: " + ex);
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

            return TryParseAutomapZoneId(currentZone.ZoneID, out coord);
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

        private void OnDestroy()
        {
            try
            {
                if (_isOpen)
                {
                    CloseWindow("OnDestroy");
                }
            }
            catch
            {
            }
        }

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
                    ": world map overlay centered on parasang (" +
                    playerLocation.X +
                    ", " +
                    playerLocation.Y +
                    ")"
                );
            }
            catch (Exception ex)
            {
                SetCaptureStatus(source + ": RenderWorldMapOverlay exception: " + ex.GetType().Name);
                Log(source + ": RenderWorldMapOverlay EXCEPTION: " + ex);
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

        private struct AutomapZoneCoord
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
                    return World + "." +
                        ParasangX + "." +
                        ParasangY + "." +
                        ZoneX + "." +
                        ZoneY + "." +
                        Z;
                }
            }
        }

        private static bool TryParseAutomapZoneId(string zoneId, out AutomapZoneCoord coord)
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

            if (!int.TryParse(parts[1], out parasangX) ||
                !int.TryParse(parts[2], out parasangY) ||
                !int.TryParse(parts[3], out zoneX) ||
                !int.TryParse(parts[4], out zoneY) ||
                !int.TryParse(parts[5], out z))
            {
                return false;
            }

            coord = new AutomapZoneCoord
            {
                World = parts[0],
                ParasangX = parasangX,
                ParasangY = parasangY,
                ZoneX = zoneX,
                ZoneY = zoneY,
                Z = z
            };

            return true;
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

                if (!TryParseAutomapZoneId(currentZone.ZoneID, out currentCoord))
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

                if (_mapContent != null)
                {
                    _mapContent.gameObject.SetActive(false);
                }

                if (_renderedZoneImage != null)
                {
                    _renderedZoneImage.gameObject.SetActive(false);
                }

                string[] pngFiles = Directory.GetFiles(automapDir, "*.png");

                int loadedCount = 0;
                int skippedCount = 0;

                for (int i = 0; i < pngFiles.Length; i++)
                {
                    string path = pngFiles[i];
                    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(path);

                    AutomapZoneCoord tileCoord;

                    if (!TryParseAutomapZoneId(fileNameWithoutExtension, out tileCoord))
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

                Log(
                    source +
                    ": loaded " +
                    loadedCount +
                    " automap tile(s), displayZ=" +
                    displayZ +
                    ", current=" +
                    currentCoord.ZoneId +
                    ", dir=" +
                    automapDir
                );
            }
            catch (Exception ex)
            {
                SetCaptureStatus(source + ": LoadCapturedZoneTilesForCurrentLayer exception: " + ex.GetType().Name);
                Log(source + ": LoadCapturedZoneTilesForCurrentLayer EXCEPTION: " + ex);
            }
        }

        private void EnsureUiCreated()
        {
            if (_root != null)
            {
                return;
            }

            UnityEngine.GameObject canvasObject = new UnityEngine.GameObject(CanvasName);
            canvasObject.transform.SetParent(this.transform, false);

            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 5000;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();

            _root = new UnityEngine.GameObject("Root");
            _root.transform.SetParent(canvasObject.transform, false);

            RectTransform rootRect = _root.AddComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            CanvasGroup rootGroup = _root.AddComponent<CanvasGroup>();
            rootGroup.alpha = 1f;
            rootGroup.interactable = true;
            rootGroup.blocksRaycasts = true;

            Image dim = _root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.78f);

            UnityEngine.GameObject frame = CreatePanel(
                "Frame",
                _root.transform,
                new Color(0.06f, 0.08f, 0.07f, 1f)
            );

            RectTransform frameRect = frame.GetComponent<RectTransform>();
            frameRect.anchorMin = new Vector2(0.04f, 0.05f);
            frameRect.anchorMax = new Vector2(0.96f, 0.95f);
            frameRect.offsetMin = Vector2.zero;
            frameRect.offsetMax = Vector2.zero;

            Outline frameOutline = frame.AddComponent<Outline>();
            frameOutline.effectDistance = new Vector2(3f, -3f);
            frameOutline.effectColor = new Color(0.45f, 0.75f, 0.55f, 1f);

            UnityEngine.GameObject inner = CreatePanel(
                "Inner",
                frame.transform,
                new Color(0.0f, 0.0f, 0.0f, 0.96f)
            );

            RectTransform innerRect = inner.GetComponent<RectTransform>();
            innerRect.anchorMin = new Vector2(0.012f, 0.018f);
            innerRect.anchorMax = new Vector2(0.988f, 0.982f);
            innerRect.offsetMin = Vector2.zero;
            innerRect.offsetMax = Vector2.zero;

            _titleText = CreateText(
                "Title",
                inner.transform,
                "CoQ Auto-Map",
                34,
                TextAnchor.MiddleLeft,
                Color.white
            );

            RectTransform titleRect = _titleText.GetComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0.025f, 0.90f);
            titleRect.anchorMax = new Vector2(0.55f, 0.985f);
            titleRect.offsetMin = Vector2.zero;
            titleRect.offsetMax = Vector2.zero;

            _layerText = CreateText(
                "LayerIndicator",
                inner.transform,
                "Layer: Surface / Z0",
                24,
                TextAnchor.MiddleRight,
                new Color(0.65f, 1f, 0.72f, 1f)
            );

            RectTransform layerRect = _layerText.GetComponent<RectTransform>();
            layerRect.anchorMin = new Vector2(0.55f, 0.90f);
            layerRect.anchorMax = new Vector2(0.975f, 0.985f);
            layerRect.offsetMin = Vector2.zero;
            layerRect.offsetMax = Vector2.zero;

            UnityEngine.GameObject viewport = CreatePanel(
                "MapViewport",
                inner.transform,
                new Color(0.02f, 0.025f, 0.02f, 1f)
            );

            RectTransform viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.anchorMin = new Vector2(0.025f, 0.155f);
            viewportRect.anchorMax = new Vector2(0.975f, 0.895f);
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;

            UnityEngine.GameObject mapPlaneObject = new UnityEngine.GameObject("MapPlane");
            mapPlaneObject.transform.SetParent(viewport.transform, false);

            _mapPlane = mapPlaneObject.AddComponent<RectTransform>();
            _mapPlane.anchorMin = new Vector2(0.5f, 0.5f);
            _mapPlane.anchorMax = new Vector2(0.5f, 0.5f);
            _mapPlane.pivot = new Vector2(0.5f, 0.5f);
            _mapPlane.anchoredPosition = Vector2.zero;
            _mapPlane.sizeDelta = Vector2.zero;
            _mapPlane.localScale = Vector3.one;

            UnityEngine.GameObject zoneTileContainerObject = new UnityEngine.GameObject("ZoneTileContainer");
            zoneTileContainerObject.transform.SetParent(mapPlaneObject.transform, false);

            _zoneTileContainer = zoneTileContainerObject.AddComponent<RectTransform>();
            _zoneTileContainer.anchorMin = new Vector2(0.5f, 0.5f);
            _zoneTileContainer.anchorMax = new Vector2(0.5f, 0.5f);
            _zoneTileContainer.pivot = new Vector2(0.5f, 0.5f);
            _zoneTileContainer.anchoredPosition = Vector2.zero;
            _zoneTileContainer.sizeDelta = Vector2.zero;
            _zoneTileContainer.localScale = Vector3.one;

            Mask mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            Outline viewportOutline = viewport.AddComponent<Outline>();
            viewportOutline.effectDistance = new Vector2(2f, -2f);
            viewportOutline.effectColor = new Color(0.25f, 0.5f, 0.35f, 1f);

            UnityEngine.GameObject content = new UnityEngine.GameObject("DummyTileGrid");
            content.transform.SetParent(viewport.transform, false);

            _mapContent = content.AddComponent<RectTransform>();
            _mapContent.anchorMin = new Vector2(0.5f, 0.5f);
            _mapContent.anchorMax = new Vector2(0.5f, 0.5f);
            _mapContent.pivot = new Vector2(0.5f, 0.5f);
            _mapContent.anchoredPosition = Vector2.zero;

            _grid = content.AddComponent<GridLayoutGroup>();
            _grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            _grid.constraintCount = VisibleColumns;
            _grid.spacing = new Vector2(2f, 2f);
            _grid.childAlignment = TextAnchor.MiddleCenter;

            CreateDummyTiles(content.transform);
            content.SetActive(false);

            UnityEngine.GameObject renderedImageObject = new UnityEngine.GameObject("RenderedZoneImage");

            renderedImageObject.transform.SetParent(mapPlaneObject.transform, false);

            RectTransform renderedImageRect = renderedImageObject.AddComponent<RectTransform>();

            _renderedZoneImageRect = renderedImageRect;

            _renderedZoneImageRect.anchorMin = new Vector2(0.5f, 0.5f);
            _renderedZoneImageRect.anchorMax = new Vector2(0.5f, 0.5f);
            _renderedZoneImageRect.pivot = new Vector2(0.5f, 0.5f);
            _renderedZoneImageRect.anchoredPosition = Vector2.zero;

            // Default size for one full Qud zone image: 80x25 cells * 16x24 pixels.
            _renderedZoneImageRect.sizeDelta = new Vector2(1280f, 600f);

            _renderedZoneImage = renderedImageObject.AddComponent<RawImage>();
            _renderedZoneImage.color = Color.white;
            _renderedZoneImage.raycastTarget = false;
            _renderedZoneImage.texture = null;

            // Keep aspect ratio so the 1280x600 zone image does not stretch.
            //_renderedZoneImage.preserveAspect = true;

            renderedImageObject.SetActive(false);

            UnityEngine.GameObject worldMapRoot = CreatePanel(
                "WorldMapOverlay",
                inner.transform,
                new Color(0.0f, 0.0f, 0.0f, 0.92f)
            );

            _worldMapRoot = worldMapRoot;

            RectTransform worldMapRootRect = worldMapRoot.GetComponent<RectTransform>();
            worldMapRootRect.anchorMin = new Vector2(0.08f, 0.18f);
            worldMapRootRect.anchorMax = new Vector2(0.92f, 0.86f);
            worldMapRootRect.offsetMin = Vector2.zero;
            worldMapRootRect.offsetMax = Vector2.zero;

            Outline worldMapOutline = worldMapRoot.AddComponent<Outline>();
            worldMapOutline.effectDistance = new Vector2(2f, -2f);
            worldMapOutline.effectColor = new Color(0.45f, 0.75f, 0.55f, 1f);

            UnityEngine.GameObject worldMapPlaneObject = new UnityEngine.GameObject("WorldMapPlane");
            worldMapPlaneObject.transform.SetParent(worldMapRoot.transform, false);

            _worldMapPlane = worldMapPlaneObject.AddComponent<RectTransform>();
            _worldMapPlane.anchorMin = new Vector2(0.5f, 0.5f);
            _worldMapPlane.anchorMax = new Vector2(0.5f, 0.5f);
            _worldMapPlane.pivot = new Vector2(0.5f, 0.5f);
            _worldMapPlane.anchoredPosition = Vector2.zero;
            _worldMapPlane.sizeDelta = new Vector2(1280f, 600f);
            _worldMapPlane.localScale = Vector3.one;

            _worldMapImage = worldMapPlaneObject.AddComponent<RawImage>();
            _worldMapImage.color = Color.white;
            _worldMapImage.raycastTarget = false;
            _worldMapImage.texture = null;

            UnityEngine.GameObject markerObject = new UnityEngine.GameObject("WorldMapTargetMarker");
            markerObject.transform.SetParent(worldMapPlaneObject.transform, false);

            _worldMapTargetMarker = markerObject.AddComponent<RectTransform>();
            _worldMapTargetMarker.anchorMin = new Vector2(0.5f, 0.5f);
            _worldMapTargetMarker.anchorMax = new Vector2(0.5f, 0.5f);
            _worldMapTargetMarker.pivot = new Vector2(0.5f, 0.5f);

            // One world-map parasang is one rendered tile: 16 x 24 pixels.
            // Slightly oversized so it reads as a marker.
            _worldMapTargetMarker.sizeDelta = new Vector2(20f, 28f);
            _worldMapTargetMarker.anchoredPosition = Vector2.zero;

            Image markerImage = markerObject.AddComponent<Image>();
            markerImage.color = new Color(0.75f, 1.0f, 0.25f, 0.18f);
            markerImage.raycastTarget = false;

            Outline markerOutline = markerObject.AddComponent<Outline>();
            markerOutline.effectDistance = new Vector2(1f, -1f);
            markerOutline.effectColor = new Color(0.9f, 1.0f, 0.35f, 0.45f);

            markerObject.SetActive(false);










            worldMapRoot.SetActive(false);





            _statusText = CreateText(
                "Status",
                inner.transform,
                "",
                20,
                TextAnchor.MiddleLeft,
                new Color(0.9f, 0.9f, 0.78f, 1f)
            );

            RectTransform statusRect = _statusText.GetComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0.025f, 0.08f);
            statusRect.anchorMax = new Vector2(0.975f, 0.145f);
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;

            _helpText = CreateText(
                "Help",
                inner.transform,
                "[Ctrl+M] toggle   [Esc] close   [W] world map   [Arrows/Numpad] pan   [PgUp/PgDn] layer   [+/-] zoom   [Home] reset   [R] render",
                18,
                TextAnchor.MiddleCenter,
                new Color(0.6f, 0.95f, 1f, 1f)
            );

            RectTransform helpRect = _helpText.GetComponent<RectTransform>();
            helpRect.anchorMin = new Vector2(0.025f, 0.015f);
            helpRect.anchorMax = new Vector2(0.975f, 0.075f);
            helpRect.offsetMin = Vector2.zero;
            helpRect.offsetMax = Vector2.zero;

            _root.SetActive(false);

            Log("EnsureUiCreated: created independent Automap overlay UI.");
        }

        private UnityEngine.GameObject CreatePanel(string name, Transform parent, Color color)
        {
            UnityEngine.GameObject panel = new UnityEngine.GameObject(name);
            panel.transform.SetParent(parent, false);

            RectTransform rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = panel.AddComponent<Image>();
            image.color = color;

            return panel;
        }

        

        private UnityEngine.UI.Text CreateText(
            string name,
            Transform parent,
            string text,
            int fontSize,
            TextAnchor alignment,
            Color color
        )
        {
            UnityEngine.GameObject textObject = new UnityEngine.GameObject(name);
            textObject.transform.SetParent(parent, false);

            RectTransform rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            UnityEngine.UI.Text uiText = textObject.AddComponent<UnityEngine.UI.Text>();
            uiText.text = text;
            uiText.fontSize = fontSize;
            uiText.alignment = alignment;
            uiText.color = color;
            uiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            uiText.verticalOverflow = VerticalWrapMode.Overflow;

            return uiText;
        }

        private void CreateDummyTiles(Transform parent)
        {
            _tiles.Clear();

            int count = VisibleColumns * VisibleRows;

            for (int i = 0; i < count; i++)
            {
                UnityEngine.GameObject tile = new UnityEngine.GameObject("Tile_" + i);
                tile.transform.SetParent(parent, false);

                Image image = tile.AddComponent<Image>();
                image.color = Color.black;

                Outline outline = tile.AddComponent<Outline>();
                outline.effectDistance = new Vector2(1f, -1f);
                outline.effectColor = new Color(0f, 0f, 0f, 0.65f);

                _tiles.Add(image);
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

            if (_mapContent != null)
            {
                _mapContent.gameObject.SetActive(false);
            }

            if (_layerText != null)
            {
                _layerText.text = "Layer: " + GetLayerLabel(_displayZ);
            }

            string currentView = "<no GameManager>";
            string currentContext = "<no NavigationController>";

            if (GameManager.Instance != null)
            {
                currentView = GameManager.Instance.CurrentGameView ?? "<null>";
            }

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

            Log(
                source +
                ": refreshed automap view. pan=(" +
                _panX +
                "," +
                _panY +
                "), offset=(" +
                _mapPlaneOffset.x.ToString("0.00") +
                "," +
                _mapPlaneOffset.y.ToString("0.00") +
                "), displayZ=" +
                _displayZ +
                ", zoom=" +
                _zoom.ToString("0.00") +
                ", view=" +
                currentView +
                ", nav=" +
                currentContext +
                "."
            );
        }

        private Color GetDummyTileColor(int x, int y, int z)
        {
            int hash = x * 73856093 ^ y * 19349663 ^ z * 83492791;
            hash = Math.Abs(hash);

            float baseR = 0.08f + ((hash & 0xFF) / 255f) * 0.30f;
            float baseG = 0.12f + (((hash >> 8) & 0xFF) / 255f) * 0.48f;
            float baseB = 0.08f + (((hash >> 16) & 0xFF) / 255f) * 0.28f;

            if ((x + y + z) % 11 == 0)
            {
                return new Color(0.65f, 0.52f, 0.18f, 1f);
            }

            if ((x * 3 + y * 5 + z) % 17 == 0)
            {
                return new Color(0.25f, 0.55f, 0.75f, 1f);
            }

            if ((x * 7 - y * 2 + z) % 23 == 0)
            {
                return new Color(0.55f, 0.20f, 0.20f, 1f);
            }

            return new Color(baseR, baseG, baseB, 1f);
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
                Log(source + ": StartCaptureCurrentZoneImage EXCEPTION: " + ex);
            }
        }

        private void StartCaptureZoneImage(Zone zone, string source, bool loadWhenComplete)
        {
            try
            {
                if (_capturePending)
                {
                    Log(
                        source +
                        ": capture already pending; skipping capture request for " +
                        (zone != null ? zone.ZoneID : "<null>")
                    );

                    return;
                }

                if (zone == null)
                {
                    if (loadWhenComplete)
                    {
                        SetCaptureStatus(source + ": no zone supplied.");
                    }
                    else
                    {
                        Log(source + ": no zone supplied.");
                    }

                    return;
                }

                string zoneId = zone.ZoneID ?? "UnknownZone";

                if (!zoneId.Contains("."))
                {
                    Log(source + ": skipped non-local/world zone capture: " + zoneId);
                    return;
                }

                if (zone.Stale)
                {
                    Log(source + ": skipped stale zone capture: " + zoneId);
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
                else
                {
                    Log(source + ": " + message);
                }

                CaptureZoneToPngQueued(zone, savePath);

                Log(source + ": queued zone capture for " + zoneId + " to " + savePath);
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

                Log(source + ": StartCaptureZoneImage EXCEPTION: " + ex);
            }
        }

        private void CaptureZoneToPngQueued(Zone zone, string savePath)
        {
            DateTime start = DateTime.Now;

            try
            {
                int zoneWidth = zone.Width;
                int zoneHeight = zone.Height;
                string zoneId = zone.ZoneID;

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

                        TimeSpan elapsed = DateTime.Now - start;

                        Log(
                            "CaptureZoneToPngQueued: wrote " +
                            zoneId +
                            " to " +
                            savePath +
                            " size=" +
                            imageWidth +
                            "x" +
                            imageHeight +
                            " elapsed=" +
                            elapsed.TotalSeconds.ToString("0.000") +
                            "s"
                        );
                    }
                    catch (Exception ex)
                    {
                        _captureError = ex.ToString();

                        Log("CaptureZoneToPngQueued UI task EXCEPTION: " + ex);
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
                _captureError = ex.ToString();
                _captureComplete = true;

                Log("CaptureZoneToPngQueued setup EXCEPTION: " + ex);
            }
        }

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
                else
                {
                    Log(message);
                }

                return;
            }

            try
            {
                TimeSpan elapsed = DateTime.Now - _captureStartTime;

                if (!_captureLoadWhenComplete)
                {
                    Log(
                        "Auto-captured zone image to disk: " +
                        _capturePath +
                        " elapsed=" +
                        elapsed.TotalSeconds.ToString("0.000") +
                        "s"
                    );

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
                Log("PollZoneCapture load EXCEPTION: " + ex);
            }
        }

        private void LoadRenderedZoneImageFromDisk(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);

            Texture2D texture = new Texture2D(
                2,
                2,
                TextureFormat.ARGB32,
                mipChain: false
            );

            //texture.filterMode = FilterMode.Point;
            texture.filterMode = UnityEngine.FilterMode.Bilinear;
            

            if (!ImageConversion.LoadImage(texture, bytes))
            {
                UnityEngine.Object.Destroy(texture);
                throw new Exception("ImageConversion.LoadImage returned false.");
            }

            if (_loadedZoneTexture != null)
            {
                UnityEngine.Object.Destroy(_loadedZoneTexture);
                _loadedZoneTexture = null;
            }

            _loadedZoneTexture = texture;

            if (_renderedZoneImage != null)
            {
                _renderedZoneImage.texture = _loadedZoneTexture;
                _renderedZoneImage.gameObject.SetActive(true);
            }

            if (_renderedZoneImageRect != null)
            {
                _renderedZoneImageRect.sizeDelta = new Vector2(texture.width, texture.height);
                _renderedZoneImageRect.anchoredPosition = Vector2.zero;
            }

            if (_mapContent != null)
            {
                _mapContent.gameObject.SetActive(false);
            }

            Log(
                "LoadRenderedZoneImageFromDisk: loaded " +
                path +
                " texture=" +
                texture.width +
                "x" +
                texture.height
            );
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
            catch (Exception ex)
            {
                Log("GetAutomapTileDirectory: The.Game.GetCacheDirectory failed: " + ex);
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

        private void SetCaptureStatus(string message)
        {
            if (_statusText != null)
            {
                _statusText.text = message;
            }

            Log(message);
        }

        
    }

    internal static class AutomapInputGatePoc
    {
        private static bool _installed;
        private static bool _blockUntilEscapeReleased;

        public static void Install()
        {
            if (_installed)
            {
                return;
            }

            _installed = true;

            try
            {
                Harmony harmony = new Harmony("CoQAutoMap.ModalInputGatePoc");

                MethodInfo keyboardPrefix = AccessTools.Method(
                    typeof(AutomapInputGatePoc),
                    nameof(BlockKeyboardGetvk)
                );

                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Keyboard)))
                {
                    if (method == null)
                    {
                        continue;
                    }

                    if (method.Name != "getvk")
                    {
                        continue;
                    }

                    if (method.ReturnType != typeof(Keys))
                    {
                        continue;
                    }

                    harmony.Patch(
                        method,
                        prefix: new HarmonyMethod(keyboardPrefix)
                    );

                    AutomapWindowPocController.DebugLog(
                        "AutomapInputGatePoc: patched Keyboard.getvk overload: " + method
                    );
                }

                MethodInfo updateQueue = AccessTools.Method(
                    typeof(ControlManager),
                    "UpdateTheCommandQueue"
                );

                if (updateQueue != null)
                {
                    harmony.Patch(
                        updateQueue,
                        prefix: new HarmonyMethod(
                            AccessTools.Method(
                                typeof(AutomapInputGatePoc),
                                nameof(BlockCommandQueueUpdate)
                            )
                        )
                    );

                    AutomapWindowPocController.DebugLog(
                        "AutomapInputGatePoc: patched ControlManager.UpdateTheCommandQueue."
                    );
                }

                MethodInfo commandDownValue = AccessTools.Method(
                    typeof(ControlManager),
                    "isCommandDownValue",
                    new Type[]
                    {
                        typeof(string),
                        typeof(bool),
                        typeof(bool),
                        typeof(bool)
                    }
                );

                if (commandDownValue != null)
                {
                    harmony.Patch(
                        commandDownValue,
                        prefix: new HarmonyMethod(
                            AccessTools.Method(
                                typeof(AutomapInputGatePoc),
                                nameof(BlockIsCommandDownValue)
                            )
                        )
                    );

                    AutomapWindowPocController.DebugLog(
                        "AutomapInputGatePoc: patched ControlManager.isCommandDownValue."
                    );
                }
            }
            catch (Exception ex)
            {
                AutomapWindowPocController.DebugLog(
                    "AutomapInputGatePoc.Install EXCEPTION: " + ex
                );

                Popup.Show(
                    "CoQ Auto-Map input gate install exception:\n\n" +
                    ex.GetType().Name +
                    "\n" +
                    ex.Message
                );
            }
        }

        public static void BlockUntilEscapeReleased()
        {
            _blockUntilEscapeReleased = true;

            try
            {
                ControlManager.ConsumeAllInput();
                ControlManager.ResetInput(false, false);
            }
            catch
            {
            }

            AutomapWindowPocController.DebugLog(
                "AutomapInputGatePoc: blocking input until Escape is released."
            );
        }

        public static void UpdateReleaseBlocks()
        {
            if (_blockUntilEscapeReleased &&
                !Input.GetKey(UnityEngine.KeyCode.Escape))
            {
                _blockUntilEscapeReleased = false;

                try
                {
                    ControlManager.ConsumeAllInput();
                    ControlManager.ResetInput(false, false);
                }
                catch
                {
                }

                AutomapWindowPocController.DebugLog(
                    "AutomapInputGatePoc: Escape released; input gate release block cleared."
                );
            }
        }

        private static bool ShouldBlockGameInput()
        {
            return AutomapWindowPocController.IsOpen || _blockUntilEscapeReleased;
        }

        private static bool BlockKeyboardGetvk(ref Keys __result)
        {

            if (!ShouldBlockGameInput())
            {
                return true;
            }

            __result = Keys.None;
            ControlManager.ConsumeAllInput();
            return false;
        }

        private static bool BlockCommandQueueUpdate()
        {
            if (!ShouldBlockGameInput())
            {
                return true;
            }

            ControlManager.ConsumeAllInput();
            return false;
        }

        private static bool BlockIsCommandDownValue(ref int __result)
        {
            if (!ShouldBlockGameInput())
            {
                return true;
            }

            __result = 0;
            ControlManager.ConsumeAllInput();
            return false;
        }
    }
}