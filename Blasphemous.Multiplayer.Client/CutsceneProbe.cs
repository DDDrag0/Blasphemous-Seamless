using Blasphemous.ModdingAPI;
using Framework.Managers;
using System;
using System.Globalization;
using Tools.DataContainer;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// PROBE DI SOLO LOG — CUTSCENE. NON E' UNA PROBE ABBANDONATA: ne restano DUE, entrambe a costo ZERO
    /// (nessuna patch Harmony). La terza — l'unica che costava una patch — e' stata TOLTA, e il perche' sta
    /// in fondo a questo commento.
    ///
    /// NON CORREGGE NULLA, NON DECIDE NULLA, NON SBLOCCO NULLA. Serve al test dell'utente per
    /// costruire la tabella `flag -> nome scena` ESEGUENDO il gioco, invece di dedurla dagli asset
    /// (che su questa macchina non sono estraibili). Il referto del progetto e'
    /// `scratch\STUDIO-CUTSCENE-CONDIVISA.md` §8; il referto d'uso e' `scratch\REFERTO-PROBE-CUTSCENE.md`;
    /// l'analisi che ha deciso COSA TENERE e COSA TOGLIERE e' `scratch\STUDIO-BOSSONE-MORTE-CAM.md`.
    ///
    /// LE DUE PROBE CHE RESTANO, e perche' servono ancora:
    ///
    ///   PROBE #0 (`[Cutscene BLK]`) — il CICLO DEL BLOCKER, a costo zero e senza patcha nulla.
    ///     Una riga SOLO QUANDO IL VALORE CAMBIA, con `HasBlocker("CINEMATIC")` (public, InputManager:210)
    ///     e `InputBlocked` (public, :23). Dice quando il blocker si accende e si spegne, e se alla fine
    ///     del test e' RIMASTO ACCESO — che e' il caso pericoloso di Cinematics.cs:134/:156/:343.
    ///     ⭐ E' LA MISURA CHE CONTA ADESSO, e per una ragione precisa: `CINEMATIC` ha DUE proprietari e
    ///     NON e' refcounted — `Cinematics` (:134/:253) e `TresAngustiasMasterBehaviour` (:614/:639/:691) —
    ///     e la riga di accensione e' **il primo segno della FSM di morte del boss**
    ///     (`cutscene_block has been enabled` e' la stessa accensione vista dal gioco). Quindi questa probe
    ///     dice se la catena di morte e' partita anche quando nessuna cutscene e' ancora nominata nel log:
    ///     e' il rischio dichiarato di M3/M4 (`scratch\STUDIO-BOSSONE-MORTE-CAM.md` §1.4-bis).
    ///     Gli altri blocker (EXECUTION, MULTIPLAYER, DEAD_SCREEN) NON si loggano: si vedono solo
    ///     nella colonna `InputBlocked`, e servono solo come contesto. Il registro resta leggibile.
    ///
    ///   PROBE #2 (`[Cutscene EV]`) — gli EVENTI PUBBLICI, zero patch: `CinematicStarted`,
    ///     `CinematicEnded`, `VideoStarted`, `VideoEnded` sono `event` pubblici di `Cinematics`
    ///     (:85-91) ⇒ basta iscriversi.
    ///     ⭐ `CinematicStarted` e' la verita' di "la chiamata e' ARRIVATA IN FONDO".
    ///     ⚠️ MA `CinematicStarted` NON prova che il contenuto sia partito: viene invocato a :148-151
    ///     ANCHE quando `PlayVideo` e' morto dentro il `catch (NullReferenceException)` di :343-347 —
    ///     e in quel caso `cinematicRunning` resta `false` per sempre, `EndCutscene` diventa un no-op
    ///     (:156) e il blocker CINEMATIC NON viene piu' spento. Il testimone di "il contenuto e'
    ///     partito davvero" e' `VideoStarted`, invocato da `OnVideoStart` (:396-405), che per i Video
    ///     arriva dal `player.started` e per Images/Animation da `OnVideoStart(null)` (:422/:456).
    ///     Per questo la probe #2 ascolta quattro eventi e non due.
    ///     ⭐ E ADESSO E' L'UNICA COSA CHE DICE SE UNA SCENA E' IN CORSO, ora che la probe #1 non c'e' piu'.
    ///
    /// LA PROBE TOLTA — era la #1 (`[Cutscene]`, un `Prefix` su `Cinematics.StartCutscene`), e NON e'
    /// stata sostituita da un `Postfix`: la riga `[Cutscene] … StartCutscene nome="…"` NON esiste piu',
    /// e se la si cerca in un log nuovo non si trova — e' voluto, non un guasto. Le ragioni, in ordine:
    ///   1. ERA L'UNICA CHE COSTAVA UNA PATCH Harmony (264 -> 263 bersagli verificati da
    ///      `tools\verify-patch-targets.ps1`): le altre due non patchano nulla e restano a costo zero.
    ///   2. LA SUA DOMANDA E' CHIUSA: serviva a costruire la tabella `flag -> nome scena`, e l'utente ha
    ///      deciso di NON fare il fix delle cutscene condivise. La tabella non serve piu' a nessuno.
    ///   3. MISURAVA LA CHIAMATA, non l'esito: nome, tipo, fade e `giaInCorso` (lo stato PRIMA di entrare).
    ///      L'esito lo dice gia' la probe #2 (`CinematicStarted`/`VideoStarted`), che resta.
    /// ⚠️ NON RIMETTERE un `Prefix` su `StartCutscene` "per completezza": e' esattamente il bersaglio
    /// che e' stato tolto, e ogni patch in piu' e' un bersaglio in piu' che puo' fallire a runtime.
    ///
    /// COME SI TOGLIE IL RESTO: cancellare questo file e le tre righe marcate `TEMPORARY (CutsceneProbe)` in
    /// `Multiplayer.cs` (OnInitialize, OnLateUpdate, OnDispose). Nessun'altra riga del progetto la cita.
    /// </summary>
    internal static class CutsceneProbe
    {
        /// <summary>
        /// PROBE #0/#2 — l'ultima riga di difesa quando una delle due fallisce.
        /// ⚠️ Il marker NON e' `[Cutscene]`: quello era della probe #1 e se n'e' andato con lei. Un marker
        /// distinto tiene separabili le righe di guasto dalle righe di misura.
        /// </summary>
        private const string MARK_FAIL = "[Cutscene ERR]";

        /// <summary>PROBE #0 — il ciclo del blocker CINEMATIC. Una riga per CAMBIO di valore.</summary>
        private const string MARK_BLOCKER = "[Cutscene BLK]";

        /// <summary>PROBE #2 — gli eventi pubblici. Dice se la chiamata e' arrivata in fondo.</summary>
        private const string MARK_EVENT = "[Cutscene EV]";

        // PROBE #0: ultimo valore letto. `_blockerRead` esiste perche' il PRIMO giro non e' un cambio:
        // una riga a ogni avvio del gioco sarebbe rumore, e il registro deve contenere solo transizioni.
        private static bool _blockerRead;
        private static bool _blockerLast;

        // PROBE #2: iscrizione. `_subscribed` impedisce la DOPPIA iscrizione (due righe per evento);
        // `_subscribePending` copre il caso in cui `Core.Cinematics` non fosse ancora pronto in
        // OnInitialize: in quel caso si riprova da Tick, una volta per frame, finche' non riesce.
        private static bool _subscribed;
        private static bool _subscribePending;
        private static bool _subscribeFailureReported;

        /// <summary>
        /// PROBE #0 + ritentativo della PROBE #2. Chiamata una volta per frame da
        /// `Multiplayer.OnLateUpdate`, che e' un ciclo che gira GIA' — nessun MonoBehaviour nuovo.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                if (_subscribePending && !_subscribed)
                    Subscribe();

                InputManager input = Core.Input;
                if (input == null)
                    return;

                bool now = input.HasBlocker("CINEMATIC");
                if (_blockerRead && now == _blockerLast)
                    return;

                _blockerRead = true;
                _blockerLast = now;

                // `InputBlocked` e' lo stato aggregato di TUTTI i blocker ed e' aggiornato SOLO dentro
                // SetBlocker (InputManager.cs:207): nessuno lo ricalcola. Sta qui come contesto.
                ModLog.Info(MARK_BLOCKER + " t=" + F(Time.time) + " CINEMATIC=" + now +
                            " InputBlocked=" + input.InputBlocked);
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// PROBE #2 — sottoscrizione agli eventi pubblici di `Cinematics`. Zero patch.
        /// Chiamata da `Multiplayer.OnInitialize`; se `Core.Cinematics` non e' ancora pronto viene
        /// ritentata da `Tick`.
        /// </summary>
        internal static void Subscribe()
        {
            try
            {
                Cinematics cinematics = Core.Cinematics;
                if (cinematics == null)
                {
                    _subscribePending = true;
                    if (!_subscribeFailureReported)
                    {
                        _subscribeFailureReported = true;
                        ModLog.Info(MARK_EVENT + " Core.Cinematics non ancora pronto: riprovo a ogni frame");
                    }
                    return;
                }

                if (_subscribed)
                    return;

                cinematics.CinematicStarted += OnCinematicStarted;
                cinematics.CinematicEnded += OnCinematicEnded;
                cinematics.VideoStarted += OnVideoStarted;
                cinematics.VideoEnded += OnVideoEnded;

                _subscribed = true;
                _subscribePending = false;

                ModLog.Info(MARK_EVENT + " probe armata: sottoscritti CinematicStarted/CinematicEnded/" +
                            "VideoStarted/VideoEnded (nessuna patch)");
            }
            catch (Exception e)
            {
                Fail("Subscribe", e);
            }
        }

        /// <summary>PROBE #2 — rimozione della sottoscrizione. Chiamata da `Multiplayer.OnDispose`.</summary>
        internal static void Unsubscribe()
        {
            try
            {
                Cinematics cinematics = Core.Cinematics;
                if (cinematics == null || !_subscribed)
                    return;

                cinematics.CinematicStarted -= OnCinematicStarted;
                cinematics.CinematicEnded -= OnCinematicEnded;
                cinematics.VideoStarted -= OnVideoStarted;
                cinematics.VideoEnded -= OnVideoEnded;

                _subscribed = false;
                _subscribePending = false;

                ModLog.Info(MARK_EVENT + " probe disarmata: sottoscrizioni rimosse");
            }
            catch (Exception e)
            {
                Fail("Unsubscribe", e);
            }
        }

        // ---- i quattro gestori. Ognuno ha il suo try/catch: sono punti di ingresso, e un'eccezione
        //      qui risalirebbe nel percorso del gioco (il gioco invoca il delegate).

        private static void OnCinematicStarted()
        {
            try
            {
                // Innescato a Cinematics.cs:148-151, cioe' in fondo a StartCutscene e DOPO il blocker.
                // Se manca dopo una riga `[Cutscene]` con giaInCorso=False: partenza MORTA prima della fine.
                ModLog.Info(MARK_EVENT + " t=" + F(Time.time) + " CinematicStarted");
            }
            catch (Exception e)
            {
                Fail("OnCinematicStarted", e);
            }
        }

        private static void OnCinematicEnded(bool cancelled)
        {
            try
            {
                // `cancelled=true` significa che l'utente ha annullato col tasto 51 (Update :100-111):
                // e' un secondo percorso di chiusura, e va visto.
                ModLog.Info(MARK_EVENT + " t=" + F(Time.time) + " CinematicEnded cancelled=" + cancelled);
            }
            catch (Exception e)
            {
                Fail("OnCinematicEnded", e);
            }
        }

        private static void OnVideoStarted(CutsceneData cutscene)
        {
            try
            {
                // ⭐ IL TESTIMONE VERO di "il contenuto e' partito": per i Video lo invoca `player.started`
                // attraverso OnVideoStart (:396), per Images e Animation `OnVideoStart(null)` (:422/:456).
                // Se questo NON arriva, `cinematicRunning` non e' mai diventato true ⇒ EndCutscene e' un
                // no-op e il blocker CINEMATIC resta acceso. Il nome puo' essere null (Images/Animation).
                string nome = Name(cutscene);
                ModLog.Info(MARK_EVENT + " t=" + F(Time.time) + " VideoStarted nome=\"" + nome + "\"");
            }
            catch (Exception e)
            {
                Fail("OnVideoStarted", e);
            }
        }

        private static void OnVideoEnded()
        {
            try
            {
                // Invocato da EndCutscene :165-168, cioe' PRIMA del fade di chiusura: il contenuto e'
                // finito. CinematicEnded puo' arrivare dopo, perche' con EndFade > 0 la chiusura e'
                // differita (:169-181).
                ModLog.Info(MARK_EVENT + " t=" + F(Time.time) + " VideoEnded");
            }
            catch (Exception e)
            {
                Fail("OnVideoEnded", e);
            }
        }

        // ---- letture e formattazione
        // ⚠️ QUI C'ERANO `CinematicRunning` (leggeva il campo privato `Cinematics.cinematicRunning` via
        // Traverse) e `Type` (il tipo della CutsceneData): esistevano SOLO per la probe #1 e sono stati
        // tolti con lei, insieme al campo `_fieldMissingReported`. `Name` e `F` restano perche' li usa la
        // probe #2, che e' viva.

        /// <summary>Nome della CutsceneData, con il `null` dichiarato invece che lanciato.</summary>
        internal static string Name(CutsceneData cutscene)
        {
            // `CutsceneData` e' uno ScriptableObject (Tools.DataContainer.CutsceneData:12), quindi `== null`
            // usa l'operatore di UnityEngine.Object: un asset distrutto risulta null anche se il
            // riferimento non lo e'. Il caso `null` e' reale e dichiarato dal referto (§3.2 trap 3).
            if (cutscene == null)
                return "<null>";

            return cutscene.name;
        }

        /// <summary>
        /// Formato dei numeri FISSO (invariante). La cultura di questa macchina puo' essere italiana e
        /// scrivere `0,5`: la chiave di lettura del registro e' l'ORDINE e il TEMPO, e un separatore
        /// che cambia renderebbe i numeri ambigui da leggere e da cercare.
        /// </summary>
        internal static string F(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// L'ULTIMA RIGA DI DIFESA, e l'unico `catch` vuoto del file — di proposito.
        /// Se fallisse anche `ModLog.Error`, l'eccezione uscirebbe dal punto di ingresso e arriverebbe
        /// DENTRO il gioco: e' l'unico danno che una probe di solo log puo' fare, e non deve accadere.
        /// </summary>
        internal static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK_FAIL + " probe fallita in " + where + " (nessun effetto sul gioco): " + e.Message);
            }
            catch
            {
            }
        }
    }
}
