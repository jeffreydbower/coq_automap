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
    public sealed class AutomapBootstrap : IPlayerMutator
    {
        public void mutate(XRL.World.GameObject player)
        {
            AutomapController.EnsureInstalled("PlayerMutator.mutate");
        }

        [CallAfterGameLoaded]
        public static void AfterGameLoaded()
        {
            AutomapController.EnsureInstalled("CallAfterGameLoaded");
        }
    }

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

        private void OpenWindow(string source)
        {
            EnsureUiCreated();

            if (_root == null)
            {
                return;
            }

            if (_isOpen)
            {
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
            catch
            {
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
        }

        private void CloseWindow(string source)
        {
            if (!_isOpen)
            {
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
            catch
            {
            }

            try
            {
                if (GameManager.Instance != null && _previousGameView != null)
                {
                    GameManager.EnsureGameView(_previousGameView);
                }
            }
            catch
            {
            }

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
            }
            catch (Exception ex)
            {
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
                        }
                        catch
                        {
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
                    return;
                }

                if (active == _automapNavigationContext && NavigationController.instance != null)
                {
                    NavigationController.instance.activeContext = null;
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

    internal static class AutomapInputGate
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
                Harmony harmony = new Harmony("CoQAutoMap.ModalInputGate");

                MethodInfo keyboardPrefix = AccessTools.Method(
                    typeof(AutomapInputGate),
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

                    AutomapController.DebugLog(
                        "AutomapInputGate: patched Keyboard.getvk overload: " + method
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
                                typeof(AutomapInputGate),
                                nameof(BlockCommandQueueUpdate)
                            )
                        )
                    );

                    AutomapController.DebugLog(
                        "AutomapInputGate: patched ControlManager.UpdateTheCommandQueue."
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
                                typeof(AutomapInputGate),
                                nameof(BlockIsCommandDownValue)
                            )
                        )
                    );

                    AutomapController.DebugLog(
                        "AutomapInputGate: patched ControlManager.isCommandDownValue."
                    );
                }
            }
            catch (Exception ex)
            {
                AutomapController.DebugLog(
                    "AutomapInputGate.Install EXCEPTION: " + ex
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

            AutomapController.DebugLog(
                "AutomapInputGate: blocking input until Escape is released."
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

                AutomapController.DebugLog(
                    "AutomapInputGate: Escape released; input gate release block cleared."
                );
            }
        }

        private static bool ShouldBlockGameInput()
        {
            return AutomapController.IsOpen || _blockUntilEscapeReleased;
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