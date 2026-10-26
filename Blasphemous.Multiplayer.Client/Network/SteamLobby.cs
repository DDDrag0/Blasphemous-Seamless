using Blasphemous.ModdingAPI;
using System;
using System.Reflection;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// Facade over Steamworks - the only class the rest of the mod names. Names no Steamworks type:
/// on a non-Steam copy those types may be missing and TypeLoadException surfaces at the call
/// site, not inside the callee. All calls into SteamLobbyImpl go through wrappers guarded by a
/// reflection probe for Steamworks.SteamAPI and a try/catch, so a missing or failing Steam never
/// escapes this class. Lobby transport only; the P2P probe is called separately but with the
/// same gate.
/// </summary>
internal static class SteamLobby
{
    /// <summary>Marker: invite-driven connection.</summary>
    internal const string MarkerJoinConnect = "[Steam] join: connecting to ";

    /// <summary>Marker: Steamworks types are absent (non-Steam copy) - IP path is unaffected.</summary>
    internal const string MarkerNoSteamworks =
        "[Steam] unavailable: Steamworks types are missing from Assembly-CSharp-firstpass " +
        "(non-Steam copy?) - the IP path is unaffected";

    /// <summary>Marker: failure caught at the boundary.</summary>
    internal const string MarkerError = "[Steam] error: ";

    private static bool _typesPresent;      // a "yes" answer is final
    private static bool _typesLogged;       // "missing" line printed once
    private static bool _tickFailureLogged; // per-frame failures must not flood
    private static long _nextProbeTicks;    // retry probe only while the "no" holds

    /// <summary>
    /// True when the Steamworks types exist in this process - probed reflectively, never named.
    /// Tries Type.GetType with the qualified name, then scans loaded assemblies (handles a
    /// renamed/differently-loaded assembly). While the answer is "no" it retries every
    /// PROBE_SECONDS, in case firstpass loads later.
    /// </summary>
    private static bool SteamworksPresent()
    {
        if (_typesPresent)
            return true;

        long now = DateTime.UtcNow.Ticks;
        if (now < _nextProbeTicks)
            return false;

        _nextProbeTicks = now + TimeSpan.TicksPerSecond * PROBE_SECONDS;

        try
        {
            if (Type.GetType(STEAM_API_TYPE + ", " + FIRSTPASS_ASSEMBLY, false) != null)
            {
                _typesPresent = true;
                return true;
            }

            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in loaded)
            {
                if (assembly == null)
                    continue;

                string name;
                try
                {
                    name = assembly.GetName().Name;
                }
                catch (Exception)
                {
                    continue;
                }

                if (name != FIRSTPASS_ASSEMBLY)
                    continue;

                if (assembly.GetType(STEAM_API_TYPE, false) != null)
                {
                    _typesPresent = true;
                    return true;
                }
            }
        }
        catch (Exception)
        {
            // A probe failure counts as "absent": conservative and does not touch the game.
        }

        if (!_typesLogged)
        {
            _typesLogged = true;
            ModLog.Info(MarkerNoSteamworks);
        }

        return false;
    }

    /// <summary>UI gate: true when Steam is present and usable. Never throws.</summary>
    internal static bool Available
    {
        get
        {
            if (!SteamworksPresent())
                return false;

            try
            {
                return QueryAvailable();
            }
            catch (Exception e)
            {
                Report("Available", e, false);
                return false;
            }
        }
    }

    /// <summary>True when a Steam lobby of ours exists right now (host).</summary>
    internal static bool HasHostLobby
    {
        get
        {
            if (!SteamworksPresent())
                return false;

            try
            {
                return QueryHasHostLobby();
            }
            catch (Exception e)
            {
                Report("HasHostLobby", e, false);
                return false;
            }
        }
    }

    /// <summary>Lobby id we are hosting (0 if none).</summary>
    internal static ulong HostLobbyId
    {
        get
        {
            if (!SteamworksPresent())
                return 0UL;

            try
            {
                return QueryHostLobbyId();
            }
            catch (Exception e)
            {
                Report("HostLobbyId", e, false);
                return 0UL;
            }
        }
    }

    /// <summary>
    /// Arms once at mod init: reads +connect_lobby from the command line and registers callbacks.
    /// If Steam is not ready yet, Tick retries.
    /// </summary>
    internal static void Arm()
    {
        if (!SteamworksPresent())
            return;

        try
        {
            CallArm();
        }
        catch (Exception e)
        {
            Report("Arm", e, false);
        }

        // The P2P probe is armed alongside the lobby: it registers P2PSessionRequest_t so a peer's
        // session request is accepted even before the probe button is pressed.
        SteamP2PProbe.Arm();
    }

    /// <summary>
    /// Per-frame cycle: callback pump and pending actions. Called from Multiplayer.OnLateUpdate.
    /// Tick failures are logged once - a per-frame error must not flood the log.
    /// </summary>
    internal static void Tick()
    {
        if (!SteamworksPresent())
            return;

        try
        {
            CallTick();
        }
        catch (Exception e)
        {
            Report("Tick", e, true);
        }

        // P2P probe advances in the same per-frame cycle: accept-session callbacks are delivered
        // before the channel is read. Separate facade, own gate and try/catch - a probe failure
        // does not stop the lobby, and while stopped the probe call is a single if.
        SteamP2PProbe.Tick();
    }

    /// <summary>Steam: Host - creates the lobby. The host session starts on LobbyCreated_t.</summary>
    internal static bool StartHost(string hostName)
    {
        if (!SteamworksPresent())
            return false;

        try
        {
            return CallStartHost(hostName ?? string.Empty);
        }
        catch (Exception e)
        {
            Report("StartHost", e, false);
            return false;
        }
    }

    /// <summary>Leaves the lobby (host or guest). Idempotent, no-op if none.</summary>
    internal static void LeaveLobby()
    {
        if (!SteamworksPresent())
            return;

        try
        {
            CallLeaveLobby();
        }
        catch (Exception e)
        {
            Report("LeaveLobby", e, false);
        }
    }

    /// <summary>Consumes the "lobby ready" (LobbyCreated_t) action exactly once.</summary>
    internal static bool TryConsumeHostLobbyReady()
    {
        if (!SteamworksPresent())
            return false;

        try
        {
            return CallTryConsumeHostLobbyReady();
        }
        catch (Exception e)
        {
            Report("TryConsumeHostLobbyReady", e, false);
            return false;
        }
    }

    /// <summary>Consumes the join address (accepted invite) exactly once.</summary>
    internal static bool TryConsumeJoin(out string serverIp, out int port)
    {
        serverIp = string.Empty;
        port = 0;

        if (!SteamworksPresent())
            return false;

        try
        {
            return CallTryConsumeJoin(out serverIp, out port);
        }
        catch (Exception e)
        {
            Report("TryConsumeJoin", e, false);
            serverIp = string.Empty;
            port = 0;
            return false;
        }
    }

    /// <summary>Discards pending Steam actions when already inside a session.</summary>
    internal static void DropPendingWhileConnected()
    {
        if (!SteamworksPresent())
            return;

        try
        {
            CallDropPendingWhileConnected();
        }
        catch (Exception e)
        {
            Report("DropPendingWhileConnected", e, false);
        }
    }

    // === Wrappers to the implementation ===
    //
    // One method per call: the JIT compiles the body when first invoked, so a TypeLoadException
    // from the implementation surfaces inside the caller's try, not before it.

    private static bool QueryAvailable()
    {
        return SteamLobbyImpl.Available;
    }

    private static bool QueryHasHostLobby()
    {
        return SteamLobbyImpl.HasHostLobby;
    }

    private static ulong QueryHostLobbyId()
    {
        return SteamLobbyImpl.HostLobbyId;
    }

    private static void CallArm()
    {
        SteamLobbyImpl.Arm();
    }

    private static void CallTick()
    {
        SteamLobbyImpl.Tick();
    }

    private static bool CallStartHost(string hostName)
    {
        return SteamLobbyImpl.StartHost(hostName);
    }

    private static void CallLeaveLobby()
    {
        SteamLobbyImpl.LeaveLobby();
    }

    private static bool CallTryConsumeHostLobbyReady()
    {
        return SteamLobbyImpl.TryConsumeHostLobbyReady();
    }

    private static bool CallTryConsumeJoin(out string serverIp, out int port)
    {
        return SteamLobbyImpl.TryConsumeJoin(out serverIp, out port);
    }

    private static void CallDropPendingWhileConnected()
    {
        SteamLobbyImpl.DropPendingWhileConnected();
    }

    /// <summary>
    /// One log line for a failure caught at the boundary. `once` is for per-frame methods: a
    /// repeating failure must not become a repeating line.
    /// </summary>
    private static void Report(string operation, Exception error, bool once)
    {
        try
        {
            if (once)
            {
                if (_tickFailureLogged)
                    return;

                _tickFailureLogged = true;
            }

            ModLog.Error(MarkerError + operation + ": " + error.GetType().Name + ": " + error.Message);
        }
        catch (Exception)
        {
            // Nothing: if the log itself fails, the game must continue.
        }
    }

    /// <summary>Fully-qualified type and assembly that must exist for Steam to be usable.</summary>
    private const string STEAM_API_TYPE = "Steamworks.SteamAPI";
    private const string FIRSTPASS_ASSEMBLY = "Assembly-CSharp-firstpass";

    /// <summary>Retry interval for the "types not present" probe.</summary>
    private const int PROBE_SECONDS = 2;
}