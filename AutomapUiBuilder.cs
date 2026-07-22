using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using XRL;
using XRL.UI;
using XRL.World;
using ConsoleLib.Console;
using XRL.World.Capabilities;
using Genkit;
using Kobold;

using NavigationContext = XRL.UI.Framework.NavigationContext;
using NavigationController = XRL.UI.Framework.NavigationController;
using FrameworkEvent = XRL.UI.Framework.Event;

//

namespace CoQAutoMap
{
    public sealed partial class AutomapController
    {
    //#####################################################    
    //Open/close/navigation context:
    //OpenWindow, CloseWindow, RegisterCommand, RestorePreviousNavigationContext
    //#####################################################

    private string _previousGameView;
    private NavigationContext _previousNavigationContext;
    private NavigationContext _automapNavigationContext;
    
    private void OpenWindow(string source)
    {
        try
        {
            EnsureUiCreated();

            if (_root == null)
            {
                Popup.Show("CoQ Auto-Map failed to create its UI root.");
                return;
            }

            if (_isOpen)
            {
                return;
            }

            AutomapZoneCoord currentCoord;

            if (!TryGetCurrentZoneCoord(out currentCoord))
            {
                Popup.Show(
                    "Atlas can only be opened while you are exploring a local zone.\n\n" +
                    "Close the world map or any special view and try again."
                );

                return;
            }

            if (RepairThumbnailCacheIfNeededThisSession(source))
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

            CenterViewOnPlayer();

            LoadAtlasLayerWithCurrentZonePriority(source);
        }
        catch (Exception ex)
        {
            Popup.Show(
                "CoQ Auto-Map open-window exception:\n\n" +
                ex.GetType().Name +
                "\n" +
                ex.Message
            );

            _isOpen = false;

            if (_root != null)
            {
                _root.SetActive(false);
            }
        }
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

        ClearPendingVisibleTileLoads();

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
            Popup.Show(
                "CoQ Auto-Map NavigationContext exception:\n\n" +
                ex.GetType().Name +
                "\n" +
                ex.Message
            );
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
                    catch (Exception ex)
                    {
                        SetCaptureStatus(
                            "Automap command failed: " +
                            commandId +
                            " / " +
                            ex.GetType().Name +
                            ": " +
                            ex.Message
                        );
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

    // Unity lifecycle cleanup.
    // This normally only matters during shutdown/reload because the controller is DontDestroyOnLoad.
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

    //#####################################################    
    //Unity UI creation:
    //EnsureUiCreated and Create*Ui helpers
    //#####################################################

    private const string CanvasName = "CoQAutoMap_Canvas";
    private Canvas _canvas;
    private UnityEngine.GameObject _root;
    private UnityEngine.UI.Text _titleText;
    private UnityEngine.UI.Text _layerText;
    private UnityEngine.UI.Text _statusText;
    private UnityEngine.UI.Text _helpText;
    private RectTransform _mapPlane;

    private RectTransform _zoneTileContainer;
    private RectTransform _thumbnailTileContainer;
    private RectTransform _fullTileContainer;
    private RectTransform _mapViewportRect;

    private bool _isDraggingMap;
    private Vector2 _lastMousePosition;

    // Builds the Unity overlay UI once and keeps it hidden until the automap opens.
    // This creates the main automap frame, the stitched-zone map plane, the world map
    // overlay, status text, and help text. Runtime open/close only toggles visibility.
    private void EnsureUiCreated()
    {
        // UI is persistent for the lifetime of the controller.
        // If it already exists, do not rebuild or duplicate Unity objects.
        if (_root != null)
        {
            return;
        }

        Transform inner = CreateAutomapShellUi();

        CreateHeaderUi(inner);       

        CreateAutomapViewportUi(inner);
        
        CreateWorldMapOverlayUi(inner);

        CreateStatusAndHelpUi(inner);

        // Start hidden. OpenWindow() toggles the root on.
        _root.SetActive(false);
    }

    private Transform CreateAutomapShellUi()
    {
        // Top-level Unity canvas.
        // ScreenSpaceOverlay means this draws directly over the game UI without needing
        // a camera. High sortingOrder keeps it above normal Qud UI.
        UnityEngine.GameObject canvasObject = new UnityEngine.GameObject(CanvasName);
        canvasObject.transform.SetParent(this.transform, false);

        _canvas = canvasObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5000;

        // Scale the UI relative to a 1920x1080 reference resolution.
        // This keeps the automap roughly proportional across different display sizes.
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Required if we later want clickable UI elements. Mostly harmless for now.
        canvasObject.AddComponent<GraphicRaycaster>();

        // Root overlay object.
        // This fills the whole screen, receives the dim background image, and is the
        // object OpenWindow/CloseWindow toggle on and off.
        _root = new UnityEngine.GameObject("Root");
        _root.transform.SetParent(canvasObject.transform, false);

        RectTransform rootRect = _root.AddComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;

        // CanvasGroup makes the root behave like a modal overlay.
        // blocksRaycasts/interactable are mostly future-proofing for mouse UI.
        CanvasGroup rootGroup = _root.AddComponent<CanvasGroup>();
        rootGroup.alpha = 1f;
        rootGroup.interactable = true;
        rootGroup.blocksRaycasts = true;

        // Full-screen dimmer behind the automap frame.
        // This visually separates the automap from the live game.
        Image dim = _root.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.78f);

        // Main outer frame.
        // This is the large visible automap window container.
        UnityEngine.GameObject frame = CreatePanel(
            "Frame",
            _root.transform,
            UiColor("#155352") // K dark grey
        );

        RectTransform frameRect = frame.GetComponent<RectTransform>();
        frameRect.anchorMin = new Vector2(0.04f, 0.05f);
        frameRect.anchorMax = new Vector2(0.96f, 0.95f);
        frameRect.offsetMin = Vector2.zero;
        frameRect.offsetMax = Vector2.zero;

        // Green outline gives the temporary UI a Qud-ish framed-panel feel.
        Outline frameOutline = frame.AddComponent<Outline>();
        frameOutline.effectDistance = new Vector2(1f, -1f);
        frameOutline.effectColor = UiColor("#b1c9c3"); // y grey

        // Inner panel.
        // This is the real content area inside the outer border.
        UnityEngine.GameObject inner = CreatePanel(
            "Inner",
            frame.transform,
            UiColor("#0f3b3a") // k dark black / Qud viridian
        );

        RectTransform innerRect = inner.GetComponent<RectTransform>();
        innerRect.anchorMin = new Vector2(0.012f, 0.018f);
        innerRect.anchorMax = new Vector2(0.988f, 0.982f);
        innerRect.offsetMin = Vector2.zero;
        innerRect.offsetMax = Vector2.zero;

        return inner.transform;
    }

    private void CreateHeaderUi(Transform parent)
    {
        float headerY = 0.945f;
        float tickTop = 0.958f;
        float tickBottom = 0.932f;

        // --------------------------
        // Left side: short stub, two ticks, long rail
        // Pattern: -| |----------
        // --------------------------

        CreateHeaderLine(
            "HeaderLeftStub",
            parent,
            new Vector2(0.024f, headerY),
            new Vector2(0.032f, headerY),
            new Vector2(0f, -1f),
            new Vector2(0f, 1f)
        );

        CreateHeaderLine(
            "HeaderLeftOuterTick",
            parent,
            new Vector2(0.036f, tickBottom),
            new Vector2(0.036f, tickTop),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        );

        CreateHeaderLine(
            "HeaderLeftInnerTick",
            parent,
            new Vector2(0.046f, tickBottom),
            new Vector2(0.046f, tickTop),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        );

        CreateHeaderLine(
            "HeaderLeftRail",
            parent,
            new Vector2(0.053f, headerY),
            new Vector2(0.462f, headerY),
            new Vector2(0f, -1f),
            new Vector2(0f, 1f)
        );

        // --------------------------
        // Center title: close brackets around Atlas
        // --------------------------

        CreateHeaderLine(
            "HeaderTitleLeftTick",
            parent,
            new Vector2(0.468f, tickBottom),
            new Vector2(0.468f, tickTop),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        );

        CreateHeaderLine(
            "HeaderTitleRightTick",
            parent,
            new Vector2(0.532f, tickBottom),
            new Vector2(0.532f, tickTop),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        );

        _titleText = CreateText(
            "Title",
            parent,
            "Atlas",
            34,
            TextAnchor.MiddleCenter,
            UiColor("#40a4b9") // c dark cyan / Qud title blue candidate
        );

        RectTransform titleRect = _titleText.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.468f, 0.900f);
        titleRect.anchorMax = new Vector2(0.532f, 0.985f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;

        // --------------------------
        // Right side: rail into layer bracket
        // --------------------------

        CreateHeaderLine(
            "HeaderRightRail",
            parent,
            new Vector2(0.538f, headerY),
            new Vector2(0.808f, headerY),
            new Vector2(0f, -1f),
            new Vector2(0f, 1f)
        );

        // Layer bracket. Wider than Surface needs, but tight enough for Subterranean N.
        CreateHeaderLine(
            "HeaderLayerLeftTick",
            parent,
            new Vector2(0.812f, tickBottom),
            new Vector2(0.812f, tickTop),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        );

        CreateHeaderLine(
            "HeaderLayerRightTick",
            parent,
            new Vector2(0.965f, tickBottom),
            new Vector2(0.965f, tickTop),
            new Vector2(-1f, 0f),
            new Vector2(1f, 0f)
        );

        CreateHeaderLine(
            "HeaderLayerTail",
            parent,
            new Vector2(0.969f, headerY),
            new Vector2(0.977f, headerY),
            new Vector2(0f, -1f),
            new Vector2(0f, 1f)
        );

        _layerText = CreateText(
            "LayerValue",
            parent,
            GetFormattedLayerName(10),
            26,
            TextAnchor.MiddleCenter,
            UiColor("#b1c9c3") // y grey
        );

        RectTransform layerValueRect = _layerText.GetComponent<RectTransform>();
        layerValueRect.anchorMin = new Vector2(0.812f, 0.900f);
        layerValueRect.anchorMax = new Vector2(0.965f, 0.985f);
        layerValueRect.offsetMin = Vector2.zero;
        layerValueRect.offsetMax = Vector2.zero;
    }
    private void CreateHeaderLine(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax
    )
    {
        UnityEngine.GameObject lineObject = new UnityEngine.GameObject(name);
        lineObject.transform.SetParent(parent, false);

        RectTransform rect = lineObject.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = lineObject.AddComponent<Image>();
        image.color = UiColor("#b1c9c3"); // y grey
        image.raycastTarget = false;
    }

    private void CreateStatusAndHelpUi(Transform parent)
    {
        // Status line.
        // Used for capture/load feedback and lightweight runtime state.
        _statusText = CreateText(
            "Status",
            parent,
            "",
            20,
            TextAnchor.MiddleLeft,
            UiColor("#b1c9c3") // y grey
        );

        //deactivating the status text
        _statusText.gameObject.SetActive(false);

        RectTransform statusRect = _statusText.GetComponent<RectTransform>();
        statusRect.anchorMin = new Vector2(0.025f, 0.08f);
        statusRect.anchorMax = new Vector2(0.975f, 0.145f);
        statusRect.offsetMin = Vector2.zero;
        statusRect.offsetMax = Vector2.zero;

        // Help line.
        // This is currently hard-coded. Later config/keybinding support should update this.
        _helpText = CreateText(
            "Help",
            parent,
            GetFormattedHelpText(),
            18,
            TextAnchor.MiddleCenter,
            UiColor("#b1c9c3") // y grey
        );

        RectTransform helpRect = _helpText.GetComponent<RectTransform>();
        helpRect.anchorMin = new Vector2(0.025f, 0.015f);
        helpRect.anchorMax = new Vector2(0.975f, 0.075f);
        helpRect.offsetMin = Vector2.zero;
        helpRect.offsetMax = Vector2.zero;
    }

    private void CreateAutomapViewportUi(Transform parent)
    {
        // Main automap viewport.
        // This is the black clipped window through which the stitched zone map is viewed.
        // The automap tiles must never draw outside this black area.
        UnityEngine.GameObject viewport = CreatePanel(
            "MapViewport",
            parent,
            Color.black
        );

        RectTransform viewportRect = viewport.GetComponent<RectTransform>();

        //for mouse integration
        _mapViewportRect = viewportRect;

        viewportRect.anchorMin = new Vector2(0.035f, 0.115f);
        viewportRect.anchorMax = new Vector2(0.965f, 0.885f);
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;

        // Mask clips the map plane to the visible viewport rectangle.
        // Without this, panned/zoomed map tiles would draw outside the map window.
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        //Outline viewportOutline = viewport.AddComponent<Outline>();
        //viewportOutline.effectDistance = new Vector2(1f, -1f);
        //viewportOutline.effectColor = QudGrey;

        // MapPlane is the panned/scaled object.
        // Pan and zoom operate on this RectTransform, not on individual zone images.
        UnityEngine.GameObject mapPlaneObject = new UnityEngine.GameObject("MapPlane");
        mapPlaneObject.transform.SetParent(viewport.transform, false);

        _mapPlane = mapPlaneObject.AddComponent<RectTransform>();
        _mapPlane.anchorMin = new Vector2(0.5f, 0.5f);
        _mapPlane.anchorMax = new Vector2(0.5f, 0.5f);
        _mapPlane.pivot = new Vector2(0.5f, 0.5f);
        _mapPlane.anchoredPosition = Vector2.zero;
        _mapPlane.sizeDelta = Vector2.zero;
        _mapPlane.localScale = Vector3.one;

        // ZoneTileContainer holds the stitched automap PNG tiles.
        // Each captured zone image becomes a child RawImage positioned by world coordinate.
        // Keeping them under one container makes pan/zoom simple.
        UnityEngine.GameObject zoneTileContainerObject = new UnityEngine.GameObject("ZoneTileContainer");
        zoneTileContainerObject.transform.SetParent(mapPlaneObject.transform, false);

        _zoneTileContainer = zoneTileContainerObject.AddComponent<RectTransform>();
        _zoneTileContainer.anchorMin = new Vector2(0.5f, 0.5f);
        _zoneTileContainer.anchorMax = new Vector2(0.5f, 0.5f);
        _zoneTileContainer.pivot = new Vector2(0.5f, 0.5f);
        _zoneTileContainer.anchoredPosition = Vector2.zero;
        _zoneTileContainer.sizeDelta = Vector2.zero;
        _zoneTileContainer.localScale = Vector3.one;

        _thumbnailTileContainer = CreateTileLayerContainer(
            "ThumbnailTileContainer",
            _zoneTileContainer.transform
        );

        _fullTileContainer = CreateTileLayerContainer(
            "FullTileContainer",
            _zoneTileContainer.transform
        );

        // Viewport border overlay.
        // Four thin line images are safer than Unity's Outline component here,
        // because Outline duplicates full Image geometry and can create a filled rectangle.
        CreateViewportBorderLine(
            "MapViewportBorderTop",
            viewport.transform,
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0f, -1f),
            new Vector2(0f, 0f)
        );

        CreateViewportBorderLine(
            "MapViewportBorderBottom",
            viewport.transform,
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 0f),
            new Vector2(0f, 1f)
        );

        CreateViewportBorderLine(
            "MapViewportBorderLeft",
            viewport.transform,
            new Vector2(0f, 0f),
            new Vector2(0f, 1f),
            new Vector2(0f, 0f),
            new Vector2(1f, 0f)
        );

        CreateViewportBorderLine(
            "MapViewportBorderRight",
            viewport.transform,
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(-1f, 0f),
            new Vector2(0f, 0f)
        );
    }

    private RectTransform CreateTileLayerContainer(string name, Transform parent)
    {
        UnityEngine.GameObject layerObject = new UnityEngine.GameObject(name);
        layerObject.transform.SetParent(parent, false);

        RectTransform rect = layerObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = Vector2.zero;
        rect.localScale = Vector3.one;

        return rect;
    }

    private void CreateViewportBorderLine(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax
    )
    {
        UnityEngine.GameObject lineObject = new UnityEngine.GameObject(name);
        lineObject.transform.SetParent(parent, false);

        RectTransform rect = lineObject.AddComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Image image = lineObject.AddComponent<Image>();
        image.color = UiColor("#b1c9c3"); // y grey
        image.raycastTarget = false;
    }

    private void CreateWorldMapOverlayUi(Transform parent)
        {
            // World map overlay root.
            // This is a modal panel drawn over the automap when the player presses W.
            // It is created once here but starts hidden.
            UnityEngine.GameObject worldMapRoot = CreatePanel(
                "WorldMapOverlay",
                parent,
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
            worldMapOutline.effectColor = UiColor("#b1c9c3"); // y grey

            // WorldMapPlane holds the rendered 80x25 world map texture.
            // Unlike the stitched automap, this is currently shown as a fixed full-map panel.
            UnityEngine.GameObject worldMapPlaneObject = new UnityEngine.GameObject("WorldMapPlane");
            worldMapPlaneObject.transform.SetParent(worldMapRoot.transform, false);

            _worldMapPlane = worldMapPlaneObject.AddComponent<RectTransform>();
            _worldMapPlane.anchorMin = new Vector2(0.5f, 0.5f);
            _worldMapPlane.anchorMax = new Vector2(0.5f, 0.5f);
            _worldMapPlane.pivot = new Vector2(0.5f, 0.5f);
            _worldMapPlane.anchoredPosition = Vector2.zero;
            _worldMapPlane.sizeDelta = new Vector2(1280f, 600f);
            _worldMapPlane.localScale = Vector3.one;

            // RawImage that displays the generated world map texture.
            // The actual texture is created/refreshed by RenderWorldMapOverlay().
            _worldMapImage = worldMapPlaneObject.AddComponent<RawImage>();
            _worldMapImage.color = Color.white;
            _worldMapImage.raycastTarget = false;
            _worldMapImage.texture = null;

            // Exact current-parasang marker.
            // The world map renderer gives the broad Qud-style highlight/falloff, but this
            // translucent rectangle makes the exact target parasang readable.
            UnityEngine.GameObject markerObject = new UnityEngine.GameObject("WorldMapTargetMarker");
            markerObject.transform.SetParent(worldMapPlaneObject.transform, false);

            _worldMapTargetMarker = markerObject.AddComponent<RectTransform>();
            _worldMapTargetMarker.anchorMin = new Vector2(0.5f, 0.5f);
            _worldMapTargetMarker.anchorMax = new Vector2(0.5f, 0.5f);
            _worldMapTargetMarker.pivot = new Vector2(0.5f, 0.5f);

            // One world-map parasang is one rendered tile: 16 x 24 pixels.
            // Slightly oversized so it reads as a marker without hiding the tile beneath it.
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
            uiText.supportRichText = true;
            uiText.fontSize = fontSize;
            uiText.alignment = alignment;
            uiText.color = color;
            uiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            uiText.verticalOverflow = VerticalWrapMode.Overflow;

            return uiText;
        }
        private string GetFormattedHelpText()
        {
            return
                GetFormattedKeyText("Ctrl+M") + " <color=#b1c9c3>Open</color>   " +
                GetFormattedKeyText("Esc") + " <color=#b1c9c3>Close</color>   " +
                GetFormattedKeyText("W") + " <color=#b1c9c3>World Map</color>   " +
                GetFormattedKeyText("Arrows/Numpad") + " <color=#b1c9c3>Pan</color>   " +
                GetFormattedKeyText("PgUp/PgDn") + " <color=#b1c9c3>Layer</color>   " +
                GetFormattedKeyText("+/-") + " <color=#b1c9c3>Zoom</color>   " +
                GetFormattedKeyText("Home") + " <color=#b1c9c3>Reset</color>";
        }

        private string GetFormattedLayerName(int z)
        {
            if (z == 10)
            {
                return "<color=#98875f>Surface</color>"; // w brown
            }

            if (z > 10)
            {
                return "<color=#a64a2e>Subterranean " + (z - 10) + "</color>";
            }

            return "<color=#40a4b9>Sky " + (10 - z) + "</color>";
        }

        private string GetFormattedKeyText(string keyText)
        {
            return
                "<color=#b1c9c3>[</color>" +
                "<color=#cfc041>" + keyText + "</color>" +
                "<color=#b1c9c3>]</color>";
        }

        private static Color UiColor(string hex)
        {
            Color color;

            if (UnityEngine.ColorUtility.TryParseHtmlString(hex, out color))
            {
                return color;
            }

            return Color.white;
        }

        //#####################################################    
        //World-map overlay:
        //ToggleWorldMapOverlay, RenderWorldMapOverlay, marker positioning
        //#####################################################

        private UnityEngine.GameObject _worldMapRoot;
        private RectTransform _worldMapPlane;
        private RawImage _worldMapImage;
        private Texture2D _worldMapTexture;
        private bool _worldMapVisible;
        private RectTransform _worldMapTargetMarker;
        
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
                SetCaptureStatus(
                    source +
                    ": world map render failed: " +
                    ex.GetType().Name +
                    ": " +
                    ex.Message
                );
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

        //#####################################################    
        //Raw keyboard controls and view state:
        //HandleRawAutomapControls, pan, zoom, layer, reset, current-Z helpers, mouse helpers
        //#####################################################

        private const float PanStep = 80f;
        // Multiplicative zoom feels better than additive zoom.
        // Zoom-out is intentionally stronger because finding offscreen tiles matters.
        private const float ZoomInFactor = 1.15f;
        private const float ZoomOutFactor = 0.82f;
        private const float MinZoom = 0.01f;
        private const float MaxZoom = 1.50f;
        private int _panX;
        private int _panY;
        // Absolute Qud Z layer currently displayed.
        // Surface is normally Z10.
        private int _displayZ = int.MinValue;
        private float _zoom = 1.0f;
        private Vector2 _mapPlaneOffset = Vector2.zero;
        
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

                if (HandleMouseAutomapControls())
                {
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
                return "Surface";
            }

            if (z > 10)
            {
                return "Subterranean " + (z - 10);
            }

            return "Sky " + (10 - z);
        }

        private bool HandleMouseAutomapControls()
        {
            if (_worldMapVisible)
            {
                _isDraggingMap = false;
                return false;
            }

            bool mouseOverMap = IsMouseOverMapViewport();

            float wheelDelta = Input.mouseScrollDelta.y;

            if (mouseOverMap && wheelDelta != 0f)
            {
                Vector2 localMousePoint;

                if (!TryGetMousePositionInMapViewport(out localMousePoint))
                {
                    return false;
                }

                float newZoom = wheelDelta > 0f
                    ? Mathf.Min(MaxZoom, _zoom * ZoomInFactor)
                    : Mathf.Max(MinZoom, _zoom * ZoomOutFactor);

                SetZoomAroundViewportPoint(
                    newZoom,
                    localMousePoint,
                    wheelDelta > 0f ? "MouseZoomIn" : "MouseZoomOut"
                );

                return true;
            }

            if (Input.GetMouseButtonDown(0) && mouseOverMap)
            {
                _isDraggingMap = true;
                _lastMousePosition = Input.mousePosition;
                return true;
            }

            if (Input.GetMouseButtonUp(0))
            {
                _isDraggingMap = false;
                return true;
            }

            if (_isDraggingMap && Input.GetMouseButton(0))
            {
                Vector2 currentMousePosition = Input.mousePosition;
                Vector2 delta = currentMousePosition - _lastMousePosition;

                if (delta.sqrMagnitude > 0.01f)
                {
                    _mapPlaneOffset += delta;
                    _lastMousePosition = currentMousePosition;
                    RefreshVisibleZoneTiles("MouseDrag");
                }

                return true;
            }

            if (_isDraggingMap && !Input.GetMouseButton(0))
            {
                _isDraggingMap = false;
            }

            return false;
        }
        private bool IsMouseOverMapViewport()
        {
            if (_mapViewportRect == null)
            {
                return false;
            }

            return RectTransformUtility.RectangleContainsScreenPoint(
                _mapViewportRect,
                Input.mousePosition
            );
        }

        private bool TryGetMousePositionInMapViewport(out Vector2 localPoint)
        {
            localPoint = Vector2.zero;

            if (_mapViewportRect == null)
            {
                return false;
            }

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _mapViewportRect,
                Input.mousePosition,
                null,
                out localPoint
            );
        }

        private void PanNorth()
        {
            _panY--;
            _mapPlaneOffset.y -= PanStep;
            RefreshVisibleZoneTiles("PanNorth");
        }

        private void PanSouth()
        {
            _panY++;
            _mapPlaneOffset.y += PanStep;
            RefreshVisibleZoneTiles("PanSouth");
        }

        private void PanWest()
        {
            _panX--;
            _mapPlaneOffset.x += PanStep;
            RefreshVisibleZoneTiles("PanWest");
        }

        private void PanEast()
        {
            _panX++;
            _mapPlaneOffset.x -= PanStep;
            RefreshVisibleZoneTiles("PanEast");
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
                RefreshVisibleZoneTiles(source);
                return;
            }

            float ratio = newZoom / oldZoom;

            // Preserve the map coordinate currently under the viewport center.
            _mapPlaneOffset *= ratio;

            _zoom = newZoom;

            RefreshVisibleZoneTiles(source);
        }

        private void CenterViewOnPlayer()
        {
            _panX = 0;
            _panY = 0;
            _mapPlaneOffset = Vector2.zero;
            _isDraggingMap = false;
            _displayZ = GetCurrentZoneZOrSurface();

            ApplyMapPlaneTransform();

            if (_layerText != null)
            {
                _layerText.text = GetFormattedLayerName(_displayZ);
            }
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

        private void SetZoomAroundViewportPoint(float newZoom, Vector2 viewportLocalPoint, string source)
        {
            float oldZoom = _zoom;

            if (oldZoom <= 0f)
            {
                oldZoom = 1f;
            }

            if (Mathf.Approximately(oldZoom, newZoom))
            {
                _zoom = newZoom;
                RefreshVisibleZoneTiles(source);
                return;
            }

            float ratio = newZoom / oldZoom;

            // Keep the map coordinate currently under viewportLocalPoint
            // under that same point after scaling.
            _mapPlaneOffset = viewportLocalPoint - (viewportLocalPoint - _mapPlaneOffset) * ratio;

            _zoom = newZoom;

            RefreshVisibleZoneTiles(source);
        }
    }
}