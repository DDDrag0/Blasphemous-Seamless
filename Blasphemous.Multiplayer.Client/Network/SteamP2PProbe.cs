using Blasphemous.ModdingAPI;
using System;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// Facade over the P2P probe - the only class the UI names. Names no Steamworks type: on a
/// non-Steam copy those types may be missing and TypeLoadException surfaces at the call site,
/// not inside the callee. Every method gates on <see cref="SteamLobby.Available"/> first, then
/// calls the implementation through a dedicated wrapper in a try/catch, so a missing or failing
/// Steam never escapes this class.
///
/// Not a transport: no game byte is sent by this class. Start/Stop toggle a measurement, Tick
/// advances it, Status returns a text line. The probe does not check whether the TCP session is
/// connected - it must work exactly when TCP fails.
/// </summary>
internal static class SteamP2PProbe
{
    /// <summary>Per-frame failures must not flood the log: written once.</summary>
    private static bool _failureLogged;

    /// <summary>True while the probe is measuring. Used by the UI for the button label.</summary>
    internal static bool Active
    {
        get
        {
            if (!SteamLobby.Available)
                return false;

            try
            {
                return QueryActive();
            }
            catch (Exception e)
            {
                Report("Active", e, false);
                return false;
            }
        }
    }

    /// <summary>
    /// Probe P2P: turns the probe on. Returns false when Steam is not available; the UI reports
    /// that. Does not check the game session - the probe must work when TCP fails. If no lobby is
    /// present it stays armed and starts on its own as soon as one appears.
    /// </summary>
    internal static bool Start()
    {
        if (!SteamLobby.Available)
            return false;

        try
        {
            return CallStart();
        }
        catch (Exception e)
        {
            Report("Start", e, false);
            return false;
        }
    }

    /// <summary>Stops the probe (and closes any P2P sessions it opened). Idempotent, never throws.</summary>
    internal static void Stop()
    {
        if (!SteamLobby.Available)
            return;

        try
        {
            CallStop();
        }
        catch (Exception e)
        {
            Report("Stop", e, false);
        }
    }

    /// <summary>
    /// Per-frame cycle (channel 0 receive, per-second heartbeat, per-3-seconds measurement).
    /// Called from SteamLobby.Tick, which is already on the mod's OnLateUpdate.
    /// </summary>
    internal static void Tick()
    {
        if (!SteamLobby.Available)
            return;

        try
        {
            CallTick();
        }
        catch (Exception e)
        {
            Report("Tick", e, true);
        }
    }

    /// <summary>Short line for the screen (empty while stopped). Returns a string, so no Steamworks type crosses the facade.</summary>
    internal static string Status()
    {
        if (!SteamLobby.Available)
            return string.Empty;

        try
        {
            return QueryStatus() ?? string.Empty;
        }
        catch (Exception e)
        {
            Report("Status", e, false);
            return string.Empty;
        }
    }

    /// <summary>
    /// Registers the P2PSessionRequest_t callback (the one that accepts sessions) at mod init, if
    /// Steam is ready. If not, Tick retries every frame: registering late is fine, never
    /// registering means every peer packet is silently discarded.
    /// </summary>
    internal static void Arm()
    {
        if (!SteamLobby.Available)
            return;

        try
        {
            CallArm();
        }
        catch (Exception e)
        {
            Report("Arm", e, false);
        }
    }

    // === Wrappers to the implementation ===
    //
    // One method per call: the JIT compiles this body when it is invoked, so a TypeLoadException
    // from the implementation surfaces inside the caller's try, not before it.

    private static bool QueryActive()
    {
        return SteamP2PProbeImpl.Active;
    }

    private static bool CallStart()
    {
        return SteamP2PProbeImpl.Start();
    }

    private static void CallStop()
    {
        SteamP2PProbeImpl.Stop();
    }

    private static void CallTick()
    {
        SteamP2PProbeImpl.Tick();
    }

    private static string QueryStatus()
    {
        return SteamP2PProbeImpl.Status();
    }

    private static void CallArm()
    {
        SteamP2PProbeImpl.Arm();
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
                if (_failureLogged)
                    return;

                _failureLogged = true;
            }

            ModLog.Error("[P2P] error: " + operation + ": " + error.GetType().Name + ": " +
                         error.Message);
        }
        catch (Exception)
        {
            // Nothing: if the log itself fails, the game must continue.
        }
    }
}