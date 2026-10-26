using System;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Displays "YOUR TEAMMATE IS PAUSED" on screen and does nothing else. Reads
    /// <c>Core.Logic.IsPaused</c> locally and sends a FLAG (not a command) on the Attack channel
    /// with <c>AttackType.PauseRelay = 203</c>, one byte payload, Reliable, heartbeat every 3s.
    /// The receiver shows or clears a sticky notification - it never pauses, resumes, opens or
    /// closes any menu, or writes TimeScale. The previous version shared pause as a command and
    /// could freeze the game permanently when a system input blocker blocked the resume; that is
    /// why the whole state machine became a flag. Co-op only: with no server nothing is sent and
    /// nothing is shown.
    /// </summary>
    internal static class PauseNotice
    {
        /// <summary>Marker on every line of this class.</summary>
        private const string MARK = "[Pausa]";

        /// <summary>
        /// Function toggle. static readonly (not const) so <c>if (!X)</c> stays reachable and the
        /// compiler does not emit CS0162.
        /// </summary>
        private static readonly bool NOTICE_ON_TEAMMATE_PAUSED = true;

        /// <summary>Notification text: English, ASCII, one line.</summary>
        private const string TEXT = "YOUR TEAMMATE IS PAUSED";

        // PauseRelay payload: [PauseRelay][local pause]. 1 = paused, 0 = running.
        private const byte FLAG_PAUSED = 1;
        private const byte FLAG_RUNNING = 0;

        // Unscaled seconds. Heartbeat is the flag carrier; timeout = 8s (two lost beats tolerated).
        private const float HEARTBEAT_SECONDS = 3f;
        private const float TIMEOUT_SECONDS = 8f;

        // Local flag state.
        private static bool _wasPaused;
        private static bool _lastSentFlag;
        private static bool _lastSentKnown;
        private static float _nextTxAt;

        // Remote flag state.
        private static bool _remotePaused;
        private static float _remoteDeadline;
        private static string _remoteFrom = "?";

        // Diagnostics.
        private static int _edges;
        private static int _sent;
        private static int _received;
        private static int _shown;
        private static int _cleared;
        private static string _lastNotSent = "";
        private static bool _toggleOffReported;

        // Coop gate state: 0 = never observed, 1 = coop, 2 = single player. Used to write one line
        // per transition instead of per frame.
        private static int _gateState;

        /// <summary>
        /// Per frame, called from <c>Multiplayer.OnLateUpdate</c> after ProcessQueue (so a wire
        /// flag applies before it is read) and after NotificationManager.Update. Order matters:
        /// gate first, local flag, then remote-timeout checks - none of them touch the game.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                if (Main.Multiplayer == null)
                    return;

                if (!NOTICE_ON_TEAMMATE_PAUSED)
                {
                    if (!_toggleOffReported)
                    {
                        _toggleOffReported = true;
                        ModLog.Info(MARK + " warning OFF by the toggle (NOTICE_ON_TEAMMATE_PAUSED=false): " +
                                    "no flag, no on-screen line, no shared pause");
                    }
                    return;
                }

                if (!CoopGate.IsMultiplayerActive())
                {
                    SinglePlayer();
                    return;
                }

                GateOpened();

                ObserveLocalFlag();
                CheckRemoteStillPaused();
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// Coop gate closed: no peer to notify, so nothing is sent. If a sticky notice was showing,
        /// it is cleared here without touching the game. Local flag is realigned silently so a
        /// later coop entry sends the true state once.
        /// </summary>
        private static void SinglePlayer()
        {
            try
            {
                if (_remotePaused)
                    Clear("no server (the gate is closed): the notice cannot stay on");

                _wasPaused = IsLocalPaused();
                _lastSentKnown = false;
                _nextTxAt = 0f;

                if (_gateState != 2)
                {
                    bool prima = _gateState == 1;
                    _gateState = 2;
                    ModLog.Info(MARK + " gate CLOSED (no server): this feature is silent - " +
                                "no flag sent, no on-screen notice, NO pause touched (the game " +
                                "pauses by itself, as it always did: in single player there is no peer " +
                                "to warn)" + (prima ? " · the connection dropped just now" : ""));
                }
            }
            catch (Exception e)
            {
                Fail("SinglePlayer", e);
            }
        }

        /// <summary>Coop gate opened: realign state so the first tick sends the true flag.</summary>
        private static void GateOpened()
        {
            try
            {
                if (_gateState == 1)
                    return;

                _gateState = 1;
                _lastSentKnown = false;
                _nextTxAt = 0f;

                ModLog.Info(MARK + " gate OPEN (server connected: host or peer): the notice applies from here - " +
                            "the mod reads `Core.Logic.IsPaused` and sends the FLAG, not a command · local state=" +
                            (IsLocalPaused() ? "pausa" : "in gioco"));
            }
            catch (Exception e)
            {
                Fail("GateOpened", e);
            }
        }

        /// <summary>
        /// Reads IsPaused every frame (seven pause paths, twelve resume paths - no single Harmony
        /// target). Sends the flag on change immediately, then at heartbeat while paused. Does not
        /// decide anything about the game.
        /// </summary>
        private static void ObserveLocalFlag()
        {
            try
            {
                bool paused = IsLocalPaused();

                if (paused != _wasPaused)
                {
                    _wasPaused = paused;
                    _edges++;
                    _nextTxAt = 0f;
                }

                // One attempt per heartbeat, not per frame: a flag that cannot go out (out of
                // level, peer elsewhere) is a postponement, not a burst.
                if (Now() < _nextTxAt)
                    return;

                bool pending = !_lastSentKnown || _lastSentFlag != paused;

                if (!pending && !paused)
                    return;

                _nextTxAt = Now() + HEARTBEAT_SECONDS;

                if (TrySendFlag(paused))
                {
                    _lastSentFlag = paused;
                    _lastSentKnown = true;
                }
            }
            catch (Exception e)
            {
                Fail("ObserveLocalFlag", e);
            }
        }

        /// <summary>
        /// Sends the flag when the guards allow it, always logging the reason. Guards use existing
        /// predicates: CurrentlyInLevel, IsConnected, ConnectedPlayersCount > 0, and - only for the
        /// "paused" flag - AnyOtherPlayerHereAndAlive (if the peer is down or elsewhere the server
        /// would drop it). The "running" flag has no such guard: clearing a notice is always valid.
        /// </summary>
        private static bool TrySendFlag(bool paused)
        {
            string tipo = paused ? "pausa locale: si'" : "pausa locale: no";

            string motivo = GateReason(paused);
            if (motivo != null)
            {
                if (_lastNotSent != motivo + "|" + paused)
                {
                    _lastNotSent = motivo + "|" + paused;
                    ModLog.Info(MARK + " flag=" + tipo + " NOT sent · motivo=" + motivo +
                                " · io=" + Me() + " · peerInScena=" + (EnemySyncManager.AnyOtherPlayerInMyScene() ? 1 : 0) +
                                "/" + EnemySyncManager.ConnectedPlayersCount() + " · fronti n." + _edges +
                                " · retried at the next heartbeat (" + HEARTBEAT_SECONDS + "s): a postponement, not a surrender");
                }
                return false;
            }

            _lastNotSent = "";
            byte[] payload = new byte[] { (byte)AttackType.PauseRelay, paused ? FLAG_PAUSED : FLAG_RUNNING };
            Main.Multiplayer.NetworkManager.SendAttackPayload(payload);
            _sent++;

            ModLog.Info(MARK + " TX flag=" + tipo + " -> canale=Attack tipo=" + (byte)AttackType.PauseRelay +
                        " verso=" + payload[1] + " · io=" + Me() +
                        " · peerInScena=" + (EnemySyncManager.AnyOtherPlayerInMyScene() ? 1 : 0) +
                        "/" + EnemySyncManager.ConnectedPlayersCount() +
                        " · inviati=" + _sent + " · fronti n." + _edges +
                        " · it is NOT a command: the peer TURNS a line on or off and stops nothing " +
                        "(no PauseGame, no menu opened or closed)" +
                        (paused ? " · the heartbeat resends it every " + HEARTBEAT_SECONDS + "s while it lasts" : ""));

            return true;
        }

        /// <summary>Flag guards in cost order. null = sendable; otherwise a reason.</summary>
        private static string GateReason(bool paused)
        {
            if (Main.Multiplayer.NetworkManager == null)
                return "network-manager-assente";
            if (!Main.Multiplayer.CurrentlyInLevel)
                return "out-of-level";
            if (!Main.Multiplayer.NetworkManager.IsConnected)
                return "not-connected";
            if (EnemySyncManager.ConnectedPlayersCount() <= 0)
                return "no-peer-connected";
            if (!paused)
                return null;

            if (!EnemySyncManager.AnyOtherPlayerHereAndAlive())
                return "peer-not-here-or-down";

            return null;
        }

        /// <summary>
        /// Wire receive. Called from NetworkManager.ReceiveAttack before the PvP branch. Payload
        /// is [attackType][flag] after the server-prefixed sender name. FLAG_PAUSED arms the
        /// notice; FLAG_RUNNING clears it. Never touches the game. Dropped when the coop gate is
        /// closed.
        /// </summary>
        internal static void ApplyFromWire(string from, byte[] data, int startIdx)
        {
            try
            {
                if (!CoopGate.IsMultiplayerActive())
                {
                    ModLog.Info(MARK + " RX flag from " + Show(from) + " DISCARDED · motivo=gate-closed " +
                                "(no server: the mod shows nothing)");
                    return;
                }

                if (data == null || startIdx < 0 || data.Length <= startIdx + 1)
                {
                    ModLog.Info(MARK + " RX MALFORMATO da " + Show(from) + ": manca il byte del flag " +
                                "(payload di " + (data == null ? 0 : data.Length) + " byte, indice " + startIdx +
                                ") - ignorato");
                    return;
                }

                byte flag = data[startIdx + 1];
                _received++;

                if (flag == FLAG_PAUSED)
                {
                    Arm(from);
                }
                else if (flag == FLAG_RUNNING)
                {
                    Clear("flag=pausa locale: no da " + Show(from) +
                          " (the teammate resumed: the notice disappears at once, the timeout is not awaited)");
                }
                else
                {
                    ModLog.Info(MARK + " RX con flag IGNOTO (" + flag + ") da " + Show(from) +
                                ": a value this build does not know - ignored");
                }
            }
            catch (Exception e)
            {
                Fail("ApplyFromWire", e);
            }
        }

        /// <summary>
        /// Arms (or refreshes) the sticky notice. Only action this class performs on the world is a
        /// text line. Text is fixed: with two players, "teammate" is enough.
        /// </summary>
        private static void Arm(string from)
        {
            try
            {
                _remoteDeadline = Now() + TIMEOUT_SECONDS;
                _remoteFrom = Show(from);

                bool gia = _remotePaused;
                _remotePaused = true;

                if (Main.Multiplayer.NotificationManager == null)
                {
                    ModLog.Error(MARK + " RX flag=pausa locale: si' da " + _remoteFrom + " ma NotificationManager " +
                                 "is unavailable: no on-screen line (the state stays on anyway)");
                    return;
                }

                // Idempotent on the same text: called every heartbeat without adding lines.
                Main.Multiplayer.NotificationManager.DisplayStickyNotification(TEXT);

                if (gia)
                    return;

                _shown++;
                ModLog.Info(MARK + " RX flag=local pause: yes from=" + _remoteFrom + " · notice ON SCREEN \"" +
                            TEXT + "\" · stays until the flag returns \"no\" or the timeout expires: " +
                            TIMEOUT_SECONDS + "s (heartbeat every " + HEARTBEAT_SECONDS + "s) · received=" + _received +
                            " · io=" + Me() + " · the mod SHOWS and does not act: no PauseGame, no menu, " +
                            "nessuna pausa condivisa (il mio gioco continua a girare)");
            }
            catch (Exception e)
            {
                Fail("Arm", e);
            }
        }

        /// <summary>
        /// Clears the notice if present. Two paths converge here: FLAG_RUNNING from the peer, or
        /// any of the local exits in CheckRemoteStillPaused. In neither case is the local pause
        /// touched - a menu opened by the player is not the network's business.
        /// </summary>
        private static void Clear(string reason)
        {
            try
            {
                bool cEra = _remotePaused;
                _remotePaused = false;
                _remoteDeadline = 0f;

                if (!cEra)
                {
                    ModLog.Info(MARK + " notice already off · motivo=" + reason + " · received=" + _received +
                                " (idempotent: there was nothing to remove)");
                    return;
                }

                try
                {
                    if (Main.Multiplayer != null && Main.Multiplayer.NotificationManager != null)
                        Main.Multiplayer.NotificationManager.ClearStickyNotification();
                }
                catch (Exception)
                {
                    // If the UI cannot be cleared, the text disappears at the next LevelLoaded.
                }

                _cleared++;
                ModLog.Info(MARK + " NOTICE REMOVED · motivo=" + reason + " · from=" + _remoteFrom +
                            " · ricevuti=" + _received + " · spenta=" + _cleared +
                            " · the local pause was NOT touched (the mod never paused anything)");
            }
            catch (Exception e)
            {
                Fail("Clear", e);
            }
        }

        /// <summary>
        /// Local exits of the notice: timeout (no beat within 8s), disconnect, back to main menu,
        /// no more connected peers, peer no longer in this room or down. Each clears the line
        /// without touching the game - the difference with the previous version, where every exit
        /// involved closing a menu and calling ResumeGame.
        /// </summary>
        private static void CheckRemoteStillPaused()
        {
            try
            {
                if (!_remotePaused)
                    return;

                if (Now() > _remoteDeadline)
                {
                    Clear("TIMEOUT of " + TIMEOUT_SECONDS + "s expired: no heartbeat of the flag from " + _remoteFrom +
                          " (game closed, dead network or a different build: the protocol does not tell builds apart, " +
                          "so the timeout is the only defence)");
                    return;
                }

                if (Main.Multiplayer.NetworkManager == null || !Main.Multiplayer.NetworkManager.IsConnected)
                {
                    Clear("disconnesso dal server");
                    return;
                }

                if (!Main.Multiplayer.CurrentlyInLevel)
                {
                    Clear("I am back at the main menu");
                    return;
                }

                if (EnemySyncManager.ConnectedPlayersCount() <= 0)
                {
                    Clear("no peer connected any more");
                    return;
                }

                if (!EnemySyncManager.AnyOtherPlayerHereAndAlive())
                {
                    Clear("the peer is no longer in this room or is down");
                }
            }
            catch (Exception e)
            {
                Fail("CheckRemoteStillPaused", e);
            }
        }

        /// <summary>Local pause state, never throwing. Read-only.</summary>
        private static bool IsLocalPaused()
        {
            try
            {
                return Core.Logic != null && Core.Logic.IsPaused;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Unscaled time: the only clock that keeps running at timeScale = 0.</summary>
        private static float Now()
        {
            return Time.unscaledTime;
        }

        private static string Me()
        {
            try
            {
                string name = Main.Multiplayer == null ? null : Main.Multiplayer.PlayerName;
                return string.IsNullOrEmpty(name) ? "(no-name)" : name;
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string Show(string name)
        {
            return string.IsNullOrEmpty(name) ? "?" : name;
        }

        /// <summary>Last catch before the mod's per-frame cycle: never rethrows.</summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " ECCEZIONE in " + where + ": " + e.GetType().Name + " - " + e.Message +
                             " (the pause notice interrupted nothing: the match went on)");
            }
            catch (Exception)
            {
                // No second place to report if the log itself fails.
            }
        }
    }
}