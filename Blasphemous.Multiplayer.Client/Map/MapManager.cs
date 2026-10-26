using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Framework.Managers;
using Framework.Map;
using Gameplay.UI.Others.MenuLogic;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.Multiplayer.Client.Map
{
    public class MapManager
    {
        // Temporarily holds the most recent player map position
        public Vector2 ActivePlayerPosition { get; set; }

        // A map update is queued whenever player changes team, enters/leaves a scene, etc.
        private bool mapUpdateQueued;

        // Skip log is throttled to once per request: this runs every frame and would otherwise
        // flood the log with 60 lines per second.
        private bool mapUpdateSkipLogged;

        // Map widget cached, with a throttle: FindObjectOfType is a scene scan and must not run per
        // frame. Throttled on unscaledTime because Time.time is frozen while paused.
        private NewMapMenuWidget mapWidget;
        private float nextMapLookupAt;
        private const float MAP_LOOKUP_INTERVAL = 0.25f;

        // UI created after first time opening the map
        private Transform playerMarks;
        private Sprite[] playerSprites;

        public void QueueMapUpdate()
        {
            mapUpdateQueued = true;
        }

        /// <summary>
        /// Serves a queued map refresh when the map tab is on screen. The guard is IsMapOnScreen,
        /// not IsPaused, because other pause tabs (inventory, tutorial, unlock) raise IsPaused
        /// without showing the map, and CreateMapUI would return false leaving playerMarks null.
        /// The queue flag is cleared BEFORE the call so an exception costs one line, not one per
        /// frame; the request is only served, never silently discarded. The catch is a second line
        /// of defense - nothing must escape Multiplayer.OnLateUpdate.
        /// </summary>
        public void Update()
        {
            if (!mapUpdateQueued)
            {
                mapUpdateSkipLogged = false;
                return;
            }

            if (!IsMapOnScreen())
            {
                // The request is NOT discarded: it stays queued. The marker says WHY, once.
                if (!mapUpdateSkipLogged)
                {
                    mapUpdateSkipLogged = true;
                    ModLog.Info("[Map] refresh request QUEUED and call SKIPPED: map tab is NOT " +
                                "on screen (CreateMapUI would return false and playerMarks would be null) · " +
                                "motivo=" + PauseCause() + " · mappaAschermo=false · the request STAYS " +
                                "queued (served, not discarded): it will be served when the map is open");
                }
                return;
            }

            // Served, not discarded - and cleared BEFORE the call: if RefreshMap threw, the event
            // would cost one line instead of a 60-per-second flood.
            mapUpdateQueued = false;
            mapUpdateSkipLogged = false;

            try
            {
                RefreshMap(false);
            }
            catch (System.Exception e)
            {
                // Second line of defense: the guard and IsMapOnScreen should make this unreachable.
                // Never let an exception escape OnLateUpdate.
                ModLog.Error("[Map] RefreshMap threw " + e.GetType().Name + ": " + e.Message +
                             " - the other player's icon stays at the previous one, the rest of the mod " +
                             "does not (this catch does not throw)");
            }
        }

        /// <summary>
        /// True when the map tab is the one SHOWING, not merely when the game is paused: five UIs
        /// raise IsPaused and only the map needs its icons refreshed. <c>activeInHierarchy</c> is
        /// the exact proof (FindObjectOfType skips inactive objects), the same test CreateMapUI
        /// does before giving up. The lookup is throttled so the scene scan does not run per frame.
        /// </summary>
        private bool IsMapOnScreen()
        {
            if (mapWidget != null && mapWidget.gameObject.activeInHierarchy)
                return true;

            // No reference (or no longer active): search, but not every frame.
            if (Time.unscaledTime < nextMapLookupAt)
                return false;

            nextMapLookupAt = Time.unscaledTime + MAP_LOOKUP_INTERVAL;
            mapWidget = Object.FindObjectOfType<NewMapMenuWidget>();
            return mapWidget != null && mapWidget.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// Reads which UI raised IsPaused, for the marker only - the decision is IsMapOnScreen.
        /// Uses the input blockers the game exposes with public HasBlocker. A new blocker would
        /// show as "pause-without-map" and would not affect the decision.
        /// </summary>
        private static string PauseCause()
        {
            try
            {
                if (!Core.Logic.IsPaused)
                    return "game-not-paused";

                if (Core.Input.HasBlocker("INVENTORY")) return "inventory-on-screen";
                if (Core.Input.HasBlocker("TUTORIAL")) return "tutorial-on-screen";
                if (Core.Input.HasBlocker("UNLOCK")) return "unlock-on-screen";
                if (Core.Input.HasBlocker("INGAME_MENU")) return "in-game-map-without-tab";
                if (Core.Input.HasBlocker("UIBLOCKING")) return "pause-menu-on-another-tab";

                return "pause-without-map";
            }
            catch (System.Exception)
            {
                return "? (blocker read failed)";
            }
        }

        public void RefreshMap(bool forceRecalculate)
        {
            // Only add marks for other players if config enabled
            if (!Main.Multiplayer.config.showPlayersOnMap)
                return;
            ModLog.Info("Updating map with new player marks!");

            // Destroy old holder to put players on top of other marks
            if (forceRecalculate && playerMarks != null)
            {
                Object.Destroy(playerMarks.gameObject);
                playerMarks = null;
            }

            // If holder doesn't exist yet, create it
            if (playerMarks == null || playerSprites == null)
            {
                // The guard is AFTER the attempt, not before: CreateMapUI returns whether it
                // succeeded, and the old code continued to playerMarks.childCount regardless,
                // throwing an NRE. If it fails, bail before touching playerMarks.
                if (!CreateMapUI())
                {
                    ModLog.Info("[Map] CreateMapUI created nothing (map tab not on screen): " +
                                "bailing BEFORE reading playerMarks - no more NRE · motivo=" +
                                PauseCause() + " · mappaAschermo=false");
                    return;
                }
            }

            // Destroy all old player marks
            for (int i = playerMarks.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(playerMarks.GetChild(i).gameObject);
            }

            // Create a new mark for each player
            foreach (PlayerStatus player in Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers)
            {
                // Only show other teams if config option
                if (player.Team != Main.Multiplayer.PlayerTeam && !Main.Multiplayer.config.showOtherTeamOnMap)
                    continue;

                // Calling this function with -1000 will calculate the center position of the scene
                Core.NewMapManager.GetCellKeyFromPosition(player.LastMapScene, new Vector2(-1000, 0));
                Vector2 cellPosition = ActivePlayerPosition;
                if (cellPosition.x < 0 || cellPosition.y < 0)
                    continue;

                // Calculate which icon to use
                Sprite icon = playerSprites[0];
                if (player.CurrentScene != player.LastMapScene)
                {
                    icon = playerSprites[1];
                }
                else if (player.Team != Main.Multiplayer.PlayerTeam)
                {
                    icon = playerSprites[2];
                }

                // Create new image for this player
                GameObject obj = new GameObject(player.Name, typeof(RectTransform));
                RectTransform rect = obj.GetComponent<RectTransform>();
                rect.SetParent(playerMarks, false);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                rect.localPosition = new Vector2(16 * cellPosition.x, 16 * cellPosition.y);
                rect.sizeDelta = new Vector2(playerSprites[0].rect.width, playerSprites[0].rect.height);
                rect.gameObject.AddComponent<Image>().sprite = icon;
                ModLog.Info($"Creating mark at " + rect.localPosition);
            }
        }

        /// <summary>
        /// Returns true only when playerMarks and playerSprites were actually created, so the
        /// caller can bail before using them. Was void; the two failure paths returned silently and
        /// the caller had no way to know.
        /// </summary>
        private bool CreateMapUI()
        {
            // Find Map widget
            NewMapMenuWidget widget = Object.FindObjectOfType<NewMapMenuWidget>();
            if (widget == null)
                return false;
            Transform rootRenderer = widget.transform.Find("Background/Map/MapMask/MapRoot/RootRenderer_0");
            if (rootRenderer == null)
                return false;

            // Keep the found widget for IsMapOnScreen.
            if (mapWidget == null)
                mapWidget = widget;

            // Create child of renderer
            playerMarks = new GameObject("PlayerMarks", typeof(RectTransform)).transform;
            playerMarks.SetParent(rootRenderer, false);

            // Get player images
            MapRendererConfig cfg = widget.RendererConfigs[0];
            playerSprites = new Sprite[3] { cfg.Marks[MapData.MarkType.Blue], cfg.Marks[MapData.MarkType.Green], cfg.Marks[MapData.MarkType.Red] };

            return true;
        }
    }
}