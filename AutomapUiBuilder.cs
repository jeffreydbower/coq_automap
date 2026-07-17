using UnityEngine;
using UnityEngine.UI;

namespace CoQAutoMap
{
    public sealed partial class AutomapController
    {
        
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
            new Color(0.06f, 0.08f, 0.07f, 1f)
        );

        RectTransform frameRect = frame.GetComponent<RectTransform>();
        frameRect.anchorMin = new Vector2(0.04f, 0.05f);
        frameRect.anchorMax = new Vector2(0.96f, 0.95f);
        frameRect.offsetMin = Vector2.zero;
        frameRect.offsetMax = Vector2.zero;

        // Green outline gives the temporary UI a Qud-ish framed-panel feel.
        Outline frameOutline = frame.AddComponent<Outline>();
        frameOutline.effectDistance = new Vector2(3f, -3f);
        frameOutline.effectColor = new Color(0.45f, 0.75f, 0.55f, 1f);

        // Inner panel.
        // This is the real content area inside the outer border.
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

        return inner.transform;
    }

    private void CreateHeaderUi(Transform parent)
    {
        // Title text.
        // Public mod name may change later, but this is the upper-left window title.
        _titleText = CreateText(
            "Title",
            parent,
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

        // Layer indicator.
        // This is updated when the player changes displayed Z layers.
        // The initial text is only a placeholder until runtime layer state is set.
        _layerText = CreateText(
            "LayerIndicator",
            parent,
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
            new Color(0.9f, 0.9f, 0.78f, 1f)
        );

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
            "[Ctrl+M] toggle   [Esc] close   [W] world map   [Arrows/Numpad] pan   [PgUp/PgDn] layer   [+/-] zoom   [Home] reset",
            18,
            TextAnchor.MiddleCenter,
            new Color(0.6f, 0.95f, 1f, 1f)
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
        // This is the clipped window through which the stitched zone map is viewed.
        UnityEngine.GameObject viewport = CreatePanel(
            "MapViewport",
            parent,
            new Color(0.02f, 0.025f, 0.02f, 1f)
        );

        RectTransform viewportRect = viewport.GetComponent<RectTransform>();
        viewportRect.anchorMin = new Vector2(0.025f, 0.155f);
        viewportRect.anchorMax = new Vector2(0.975f, 0.895f);
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;

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

        // Mask clips the map plane to the visible viewport rectangle.
        // Without this, panned/zoomed map tiles would draw outside the map window.
        Mask mask = viewport.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        // Viewport border.
        Outline viewportOutline = viewport.AddComponent<Outline>();
        viewportOutline.effectDistance = new Vector2(2f, -2f);
        viewportOutline.effectColor = new Color(0.25f, 0.5f, 0.35f, 1f);
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
            worldMapOutline.effectColor = new Color(0.45f, 0.75f, 0.55f, 1f);

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
            uiText.fontSize = fontSize;
            uiText.alignment = alignment;
            uiText.color = color;
            uiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
            uiText.verticalOverflow = VerticalWrapMode.Overflow;

            return uiText;
        }
    }
}