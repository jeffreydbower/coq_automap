using System;
using System.Collections.Generic;
using UnityEngine;
using XRL;
using XRL.UI;
using XRL.World;

using NavigationContext = XRL.UI.Framework.NavigationContext;
using NavigationController = XRL.UI.Framework.NavigationController;
using FrameworkEvent = XRL.UI.Framework.Event;

namespace CoQAutoMap
{
    public sealed partial class AutomapController
    {
        
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
    }
}