using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Hacknet;
using HarmonyLib;
using Microsoft.Xna.Framework.Input;

namespace HacknetAccess.Patches
{
    /// <summary>
    /// Patches for network map accessibility.
    /// F3 toggles map focus — first press reads all nodes and enters map mode.
    /// Up/Down navigates nodes, Enter connects, Escape returns to terminal.
    /// Announces new node discovery.
    /// </summary>
    [HarmonyPatch]
    internal static class NetworkMapPatches
    {
        private static int _lastVisibleNodeCount;
        private static int _focusedNodeIndex = -1;
        private static int _lastAnnouncedFocus = -1;
        private static int _pendingConnect = -1;
        private static bool _mapHasFocus;
        private static KeyboardState _prevKeyState;

        /// <summary>
        /// Prefix on NetworkMap.Draw — handle navigation when map has focus.
        /// </summary>
        [HarmonyPatch]
        static class DrawPrefix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.NetworkMap"),
                    "Draw", new[] { typeof(float) });
            }

            static void Prefix(object __instance, List<int> ___visibleNodes)
            {
                var ks = Keyboard.GetState();

                if (___visibleNodes == null || ___visibleNodes.Count == 0 || !_mapHasFocus)
                {
                    _prevKeyState = ks;
                    return;
                }

                // Up/Down to navigate nodes
                if (ks.IsKeyDown(Keys.Up) && _prevKeyState.IsKeyUp(Keys.Up))
                {
                    _focusedNodeIndex--;
                    if (_focusedNodeIndex < 0) _focusedNodeIndex = ___visibleNodes.Count - 1;
                    AnnounceNode(__instance, ___visibleNodes, _focusedNodeIndex);
                }
                else if (ks.IsKeyDown(Keys.Down) && _prevKeyState.IsKeyUp(Keys.Down))
                {
                    _focusedNodeIndex++;
                    if (_focusedNodeIndex >= ___visibleNodes.Count) _focusedNodeIndex = 0;
                    AnnounceNode(__instance, ___visibleNodes, _focusedNodeIndex);
                }

                // Enter to connect to focused node
                if (ks.IsKeyDown(Keys.Enter) && _prevKeyState.IsKeyUp(Keys.Enter)
                    && _focusedNodeIndex >= 0 && _focusedNodeIndex < ___visibleNodes.Count)
                {
                    _pendingConnect = ___visibleNodes[_focusedNodeIndex];
                    _mapHasFocus = false;
                    Plugin.Announce(Loc.Get("network.focusTerminal"), false);
                }

                // Escape returns focus to terminal
                if (ks.IsKeyDown(Keys.Escape) && _prevKeyState.IsKeyUp(Keys.Escape))
                {
                    _mapHasFocus = false;
                    Plugin.Announce(Loc.Get("network.focusTerminal"));
                }

                _prevKeyState = ks;
            }
        }

        /// <summary>
        /// Postfix on NetworkMap.Draw — check for new nodes, handle pending connect,
        /// force GuiData.hot for focused node button.
        /// </summary>
        [HarmonyPatch]
        static class DrawPostfix
        {
            static MethodBase TargetMethod()
            {
                return AccessTools.Method(
                    AccessTools.TypeByName("Hacknet.NetworkMap"),
                    "Draw", new[] { typeof(float) });
            }

            static void Postfix(object __instance, List<int> ___visibleNodes)
            {
                if (___visibleNodes == null) return;

                // Announce new nodes
                int currentCount = ___visibleNodes.Count;
                if (currentCount > _lastVisibleNodeCount && _lastVisibleNodeCount > 0)
                {
                    int newNodes = currentCount - _lastVisibleNodeCount;
                    Plugin.Announce(Loc.Get("network.newNodes", newNodes), false);
                }
                _lastVisibleNodeCount = currentCount;

                // Handle pending connect
                if (_pendingConnect >= 0)
                {
                    int nodeIndex = _pendingConnect;
                    _pendingConnect = -1;

                    try
                    {
                        var osType = AccessTools.TypeByName("Hacknet.OS");
                        var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                        if (os != null)
                        {
                            var nodesField = AccessTools.Field(__instance.GetType(), "nodes");
                            var nodes = nodesField?.GetValue(__instance) as System.Collections.IList;
                            if (nodes != null && nodeIndex >= 0 && nodeIndex < nodes.Count)
                            {
                                var computer = nodes[nodeIndex];
                                string ip = AccessTools.Field(computer.GetType(), "ip")
                                    ?.GetValue(computer) as string;
                                if (!string.IsNullOrEmpty(ip))
                                {
                                    var runCommand = AccessTools.Method(osType, "runCommand",
                                        new[] { typeof(string) });
                                    runCommand?.Invoke(os, new object[] { "connect " + ip });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log(LogCategory.Handler, "NetworkMap",
                            $"Connect failed: {ex.Message}");
                    }
                }

                // Set GuiData.hot for focused node button
                if (_mapHasFocus && _focusedNodeIndex >= 0 && _focusedNodeIndex < ___visibleNodes.Count)
                {
                    GuiData.hot = 2000 + ___visibleNodes[_focusedNodeIndex];
                }
            }
        }

        /// <summary>
        /// Announce a node at the given visible index.
        /// </summary>
        private static void AnnounceNode(object netMap, List<int> visibleNodes, int visibleIndex)
        {
            if (visibleIndex < 0 || visibleIndex >= visibleNodes.Count) return;
            int nodeIndex = visibleNodes[visibleIndex];

            try
            {
                var nodes = AccessTools.Field(netMap.GetType(), "nodes")
                    ?.GetValue(netMap) as System.Collections.IList;
                if (nodes == null || nodeIndex < 0 || nodeIndex >= nodes.Count) return;

                var computer = nodes[nodeIndex];
                string name = AccessTools.Field(computer.GetType(), "name")
                    ?.GetValue(computer) as string;
                string ip = AccessTools.Field(computer.GetType(), "ip")
                    ?.GetValue(computer) as string;
                string adminIP = AccessTools.Field(computer.GetType(), "adminIP")
                    ?.GetValue(computer) as string;

                var osType = AccessTools.TypeByName("Hacknet.OS");
                var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
                string myIP = null;
                if (os != null)
                {
                    var thisComp = AccessTools.Field(osType, "thisComputer")?.GetValue(os);
                    myIP = AccessTools.Field(thisComp?.GetType(), "ip")?.GetValue(thisComp) as string;
                }

                string status = "";
                if (ip == myIP)
                    status = " (your computer)";
                else if (adminIP == myIP)
                    status = " (admin)";

                int position = visibleIndex + 1;
                Plugin.Announce(Loc.Get("network.node", name, ip, position, visibleNodes.Count, status));
                _lastAnnouncedFocus = visibleIndex;
            }
            catch (Exception ex)
            {
                DebugLogger.Log(LogCategory.Handler, "NetworkMap",
                    $"AnnounceNode failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Process F3 shortcut — toggles map focus.
        /// First press: reads all nodes and enters map navigation mode.
        /// Subsequent press while in map: reads all nodes again.
        /// Called from Plugin.ProcessInput().
        /// </summary>
        public static void ProcessInput(KeyboardState currentState)
        {
            if (!Plugin.IsKeyPressed(Keys.F3, currentState)) return;

            DebugLogger.LogInput("F3", "Network map");

            var osType = AccessTools.TypeByName("Hacknet.OS");
            var os = AccessTools.Field(osType, "currentInstance")?.GetValue(null);
            if (os == null)
            {
                Plugin.Announce(Loc.Get("network.unavailable"));
                return;
            }

            var netMap = AccessTools.Field(osType, "netMap")?.GetValue(os);
            if (netMap == null)
            {
                Plugin.Announce(Loc.Get("network.unavailable"));
                return;
            }

            var nodes = AccessTools.Field(netMap.GetType(), "nodes")
                ?.GetValue(netMap) as System.Collections.IList;
            var visibleNodes = AccessTools.Field(netMap.GetType(), "visibleNodes")
                ?.GetValue(netMap) as List<int>;

            if (nodes == null || visibleNodes == null || visibleNodes.Count == 0)
            {
                Plugin.Announce(Loc.Get("network.unavailable"));
                return;
            }

            // Enter map focus mode
            _mapHasFocus = true;
            if (_focusedNodeIndex < 0 || _focusedNodeIndex >= visibleNodes.Count)
                _focusedNodeIndex = 0;

            string myIP = null;
            var thisComp = AccessTools.Field(osType, "thisComputer")?.GetValue(os);
            myIP = AccessTools.Field(thisComp?.GetType(), "ip")?.GetValue(thisComp) as string;

            var sb = new StringBuilder();
            sb.Append(Loc.Get("network.header", visibleNodes.Count));
            sb.Append(" ");
            sb.Append(Loc.Get("network.instructions"));

            Plugin.Announce(sb.ToString());
        }


        /// <summary>
        /// Whether the network map currently has keyboard focus.
        /// </summary>
        public static bool HasFocus => _mapHasFocus;

        /// <summary>
        /// Reset focus state.
        /// </summary>
        public static void Reset()
        {
            _focusedNodeIndex = -1;
            _lastAnnouncedFocus = -1;
            _pendingConnect = -1;
            _mapHasFocus = false;
        }
    }
}
