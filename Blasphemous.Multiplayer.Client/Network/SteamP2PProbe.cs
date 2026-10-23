using Blasphemous.ModdingAPI;
using System;

namespace Blasphemous.Multiplayer.Client.Network;

/// <summary>
/// ⭐ LA FACCIATA DELLA SONDA P2P — l'unica classe che la UI nomina.
///
/// QUESTA CLASSE NON NOMINA NESSUN TIPO `Steamworks`: né un campo, né un parametro, né una variabile
/// locale. Vale la stessa regola di `SteamLobby` (vedi il commento in testa a quel file), e per la
/// stessa ragione concreta: su una copia **non-Steam** (GOG &amp; co.) `Assembly-CSharp-firstpass` può
/// non avere affatto i tipi `Steamworks`. Un tipo assente non produce "una funzione che non c'è":
/// produce una **`TypeLoadException`** lanciata **quando la JIT compila il metodo che nomina quel
/// tipo** — cioè al **punto di chiamata**. Ecco perché:
///
///   · ogni metodo qui dentro, **prima** di toccare `SteamP2PProbeImpl`, chiede a `SteamLobby.Available`
///     se i tipi ci sono davvero (è la sonda per riflessione che `SteamLobby` fa già): se non ci sono,
///     l'implementazione non viene **mai** nominata, la sua JIT non parte, e niente eccezione;
///   · la chiamata sta in **un metodo suo**, dentro un `try/catch`: è lì che la `TypeLoadException`
///     nasce, quindi è lì che si cattura. Un `try/catch` scritto *dentro* l'implementazione non
///     verrebbe mai raggiunto.
///
/// ⛔ E NON È UN TRASPORTO: nessun metodo di questa classe manda un byte di gioco. `Start`/`Stop`
/// accendono una **misura**; `Tick` la fa avanzare; `Status` restituisce una riga di testo. La sonda
/// non chiede mai se il TCP è connesso: deve funzionare proprio quando il TCP fallisce (secondo PC su
/// un hotspot). Vedi `SteamP2PProbeImpl`.
///
/// ⚠️ SE STEAM NON C'È: `Available` è `false`, il bottone è **disabilitato** (non nascosto: nasconderlo
/// farebbe credere che la funzione non esista) e nessun metodo qui sotto tocca l'implementazione.
/// </summary>
internal static class SteamP2PProbe
{
    /// <summary>Un guasto per frame non deve inondare il log: si scrive una volta sola.</summary>
    private static bool _failureLogged;

    /// <summary>"La sonda sta misurando adesso?" — usato dalla UI per la parola del bottone.</summary>
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
    /// "PROBE P2P" — accende la sonda. `false` = non si è potuto nemmeno chiedere (Steam non c'è):
    /// la UI lo dice con una notifica, e non succede nient'altro.
    ///
    /// ⚠️ NON SI CHIEDE "SONO CONNESSO AL GIOCO?": la sonda deve misurare anche — e soprattutto —
    /// quando il TCP non arriva. L'unico requisito è una lobby Steam, e se non c'è la sonda resta accesa
    /// e comincia da sola appena si entra (vedi `SteamP2PProbeImpl.RequireLobby`).
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

    /// <summary>Spegne la sonda (e chiude le sessioni P2P che aveva aperto). Idempotente, non lancia.</summary>
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
    /// Il ciclo per frame (ricezione a canale 0, battito ogni secondo, misura ogni 3). Chiamato da
    /// `SteamLobby.Tick()`, che è già dentro l'`OnLateUpdate` della mod: **zero bersagli Harmony nuovi,
    /// zero MonoBehaviour nuovi**.
    ///
    /// ⚠️ Il guasto si logga **una volta sola**: questo metodo è per frame.
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

    /// <summary>
    /// La riga breve per la schermata (vuota a sonda ferma). Restituisce una **stringa**, quindi
    /// nessun tipo `Steamworks` attraversa questa facciata — e nessuno può trattenerla per sbaglio.
    /// </summary>
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
    /// Registra la callback `P2PSessionRequest_t` (quella che accetta le sessioni) all'avvio della mod,
    /// se Steam è già pronto. Se non lo è, `Tick` ci riprova ogni frame: registrarla tardi va bene,
    /// **non** registrarla mai no (senza, ogni pacchetto del peer viene scartato in silenzio).
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

    // ─── I GUSCI VERSO L'IMPLEMENTAZIONE ─────────────────────────────────────────────────────────
    //
    // ⚠️ UN METODO PER CHIAMATA, come in `SteamLobby`: è qui che la `TypeLoadException` nasce (la JIT
    // compila questo corpo quando viene invocato), quindi è qui che il `try/catch` del chiamante la
    // cattura. Se queste righe stessero dentro `Start`/`Tick`/..., la JIT caricherebbe l'implementazione
    // compilando QUEI metodi, cioè prima del `try`: un `catch` che non cattura mai niente.

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
    /// Una riga di log per un guasto catturato al confine. `once` è per i metodi per frame: un guasto
    /// che si ripete 60 volte al secondo non deve diventare 60 righe al secondo.
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

            ModLog.Error("[P2P] errore: " + operation + ": " + error.GetType().Name + ": " +
                         error.Message);
        }
        catch (Exception)
        {
            // Niente: se anche il log esplode, il gioco deve continuare.
        }
    }
}
