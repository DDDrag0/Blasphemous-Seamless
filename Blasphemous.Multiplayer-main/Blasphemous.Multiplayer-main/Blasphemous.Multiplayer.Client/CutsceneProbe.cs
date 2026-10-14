using Blasphemous.ModdingAPI;
using Framework.Managers;
using HarmonyLib;
using System;
using System.Globalization;
using Tools.DataContainer;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// PROBE DI SOLO LOG — CUTSCENE — 15 ottobre 2026. TEMPORANEA: SI TOGLIE IN UN PUNTO SOLO.
    ///
    /// NON CORREGGE NULLA, NON DECIDE NULLA, NON SBLOCCO NULLA. Serve al test dell'utente per
    /// costruire la tabella `flag -> nome scena` ESEGUENDO il gioco, invece di dedurla dagli asset
    /// (che su questa macchina non sono estraibili). Il referto del progetto e'
    /// `scratch\STUDIO-CUTSCENE-CONDIVISA.md` §8; il referto d'uso e' `scratch\REFERTO-PROBE-CUTSCENE.md`.
    ///
    /// LE TRE PROBE, e perche' servono tutte e tre:
    ///
    ///   PROBE #1 (`[Cutscene]`, un Prefix su Cinematics.StartCutscene) — la CHIAMATA: nome della
    ///     scena, tipo, fade, e `giaInCorso`, cioe' lo stato PRIMA di entrare. E' l'unico campo che
    ///     distingue una partenza ACCETTATA da una RIFIUTATA dal gioco (`if (cinematicRunning) return;`
    ///     sta a Cinematics.cs:115).
    ///     PERCHE' UN PREFIX E NON UN POSTFIX — verificato in IL, non per stile: `StartCutscene` NON
    ///     accende `cinematicRunning`; lo accende dopo, dentro `PlayVideo`. Per le Images con
    ///     `fadeStart == 0` la coroutine `ShowImagesCutscene` gira SINCRONA fino al primo `yield`
    ///     (:426) e `cinematicRunning = true` e' a :420 ⇒ un POSTFIX leggerebbe `true` su una
    ///     partenza legittima e la misura sarebbe falsa.
    ///     PERCHE' `void` — un `void Prefix` non puo' ritornare `false`, e solo un `bool Prefix` che
    ///     ritorna `false` salta l'originale. La garanzia e' STRUTTURALE, non una promessa.
    ///     PERCHE' `__0..__4` — Harmony lega per nome: un rename del parametro romperebbe la patch.
    ///     Il binding posizionale sopravvive al rename.
    ///
    ///   PROBE #0 (`[Cutscene BLK]`) — il CICLO DEL BLOCKER, a costo zero e senza patcha nulla.
    ///     Una riga SOLO QUANDO IL VALORE CAMBIA, con `HasBlocker("CINEMATIC")` (public, InputManager:210)
    ///     e `InputBlocked` (public, :23). Dice quando il blocker si accende e si spegne, e se alla fine
    ///     del test e' RIMASTO ACCESO — che e' il caso pericoloso di Cinematics.cs:134/:156/:343.
    ///     ⭐ E DISTINGUE LE DUE ACCENSIONI: `CINEMATIC` ha DUE proprietari e NON e' refcounted —
    ///     `Cinematics` (:134/:253) e `TresAngustiasMasterBehaviour` (:614/:639/:691). Una riga
    ///     `CINEMATIC=True` SENZA la riga `[Cutscene]` della probe #1 e' il BOSS, non una cutscene.
    ///     Gli altri blocker (EXECUTION, MULTIPLAYER, DEAD_SCREEN) NON si loggano: si vedono solo
    ///     nella colonna `InputBlocked`, e servono solo come contesto. Il registro resta leggibile.
    ///
    ///   PROBE #2 (`[Cutscene EV]`) — gli EVENTI PUBBLICI, zero patch: `CinematicStarted`,
    ///     `CinematicEnded`, `VideoStarted`, `VideoEnded` sono `event` pubblici di `Cinematics`
    ///     (:85-91) ⇒ basta iscriversi.
    ///     ⭐ `CinematicStarted` e' la verita' di "la chiamata e' ARRIVATA IN FONDO": se il prefix dice
    ///     si' e questo evento NON arriva, la partenza e' morta prima ⇒ e' il caso pericoloso.
    ///     ⚠️ MA `CinematicStarted` NON prova che il contenuto sia partito: viene invocato a :148-151
    ///     ANCHE quando `PlayVideo` e' morto dentro il `catch (NullReferenceException)` di :343-347 —
    ///     e in quel caso `cinematicRunning` resta `false` per sempre, `EndCutscene` diventa un no-op
    ///     (:156) e il blocker CINEMATIC NON viene piu' spento. Il testimone di "il contenuto e'
    ///     partito davvero" e' `VideoStarted`, invocato da `OnVideoStart` (:396-405), che per i Video
    ///     arriva dal `player.started` e per Images/Animation da `OnVideoStart(null)` (:422/:456).
    ///     Per questo la probe #2 ascolta quattro eventi e non due.
    ///
    /// COME SI TOGLIE: cancellare questo file e le tre righe marcate `TEMPORARY (CutsceneProbe)` in
    /// `Multiplayer.cs` (OnInitialize, OnLateUpdate, OnDispose). Nessun'altra riga del progetto la cita.
    /// </summary>
    internal static class CutsceneProbe
    {
        /// <summary>PROBE #1 — la chiamata a StartCutscene. E' la riga da cercare per la tabella.</summary>
        private const string MARK_START = "[Cutscene]";

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

        /// <summary>
        /// Legge il campo PRIVATO `CinematicData.cinematicRunning` (:24) via Traverse — e' cio' che
        /// distingue "PARTITA" da "RIFIUTATA": se era gia' `true`, il gioco esce subito a :115.
        ///
        /// ⚠️ SE IL CAMPO NON ESISTESSE, Traverse restituirebbe `false` in silenzio — e ogni riga
        /// direbbe `giaInCorso=False`, cioe' la misura sarebbe falsa nel modo peggiore: plausibile.
        /// Per questo `FieldExists()` e' controllato e il valore diventa `?campo-assente?`, che non si
        /// puo' confondere con un booleano.
        /// </summary>
        internal static string CinematicRunning(Cinematics instance)
        {
            if (instance == null)
                return "?istanza-nulla?";

            Traverse field = Traverse.Create(instance).Field("cinematicRunning");
            if (!field.FieldExists())
            {
                if (!_fieldMissingReported)
                {
                    _fieldMissingReported = true;
                    ModLog.Error(MARK_START + " campo privato Cinematics.cinematicRunning NON trovato: " +
                                 "giaInCorso non e' leggibile e la colonna va considerata non valida");
                }
                return "?campo-assente?";
            }

            return field.GetValue<bool>().ToString();
        }

        private static bool _fieldMissingReported;

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

        /// <summary>Tipo della cutscene, senza cast: e' un enum del gioco (Video | Images | Animation).</summary>
        internal static string Type(CutsceneData cutscene)
        {
            if (cutscene == null)
                return "<null>";

            return cutscene.cinematicType.ToString();
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
                ModLog.Error(MARK_START + " probe fallita in " + where + " (nessun effetto sul gioco): " + e.Message);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// PROBE #1 — `Prefix` su `Cinematics.StartCutscene`. SOLO LOG (vedi CutsceneProbe).
    ///
    /// Firma verificata in IL (`scratch\_il\Framework.Managers\Cinematics.cs:113`):
    ///     public void StartCutscene(CutsceneData cutscene, bool muteAudio, float fadeStart, float fadeEnd, bool useBackground)
    /// `public`, UNA SOLA definizione in tutti i 2079 file decompilati (nessun overload), non virtual,
    /// metodo d'istanza di `Cinematics : GameSystem` ⇒ Harmony la patcha senza cerimonie.
    /// Il primo parametro si chiama `cutscene`, ma qui si lega per POSIZIONE (`__0`): piu' robusto.
    ///
    /// `void Prefix` ⇒ NON PUO' ritornare `false` ⇒ NON PUO' saltare l'originale.
    /// Nessun argomento `ref`, nessuna scrittura, nessun blocker, nessuna chiamata in piu'.
    /// </summary>
    [HarmonyPatch(typeof(Framework.Managers.Cinematics), "StartCutscene")]
    class Cinematics_StartCutscene_Probe
    {
        public static void Prefix(CutsceneData __0, bool __1, float __2, float __3, bool __4, Cinematics __instance)
        {
            try
            {
                // Formato richiesto dal referto §8.2, campo per campo. `tipo=` e' in fondo, in piu':
                // il tipo decide QUALE dei tre percorsi di StartCutscene viene preso, e quindi quale
                // finestra del blocker si apre (§8.4 griglia 1) - senza, la probe e' monca.
                ModLog.Info(
                    "[Cutscene] t=" + CutsceneProbe.F(Time.time) +
                    " StartCutscene nome=\"" + CutsceneProbe.Name(__0) + "\"" +
                    " mute=" + __1 +
                    " fadeStart=" + CutsceneProbe.F(__2) +
                    " fadeEnd=" + CutsceneProbe.F(__3) +
                    " bg=" + __4 +
                    " giaInCorso=" + CutsceneProbe.CinematicRunning(__instance) +
                    " tipo=" + CutsceneProbe.Type(__0));
            }
            catch (Exception e)
            {
                CutsceneProbe.Fail("Prefix(Cinematics.StartCutscene)", e);
            }
        }
    }
}
