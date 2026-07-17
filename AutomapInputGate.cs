using System;
using System.Reflection;
using ConsoleLib.Console;
using HarmonyLib;
using UnityEngine;
using XRL.UI;

namespace CoQAutoMap
{
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