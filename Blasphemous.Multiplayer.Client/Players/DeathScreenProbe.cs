using Blasphemous.ModdingAPI;
using Com.LuisPedroFonseca.ProCamera2D;
using Framework.Managers;
using Gameplay.UI.Others.Buttons;
using System;
using System.Globalization;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// PASSO 0 — LA MISURA DELLA SCHERMATA DI MORTE. **Solo log: non cambia NULLA del gioco.**
    ///
    /// PERCHE' ESISTE. Lo studio `scratch\STUDIO-DEATH-SCREEN-TRASPARENTE.md` ha dimostrato che la
    /// schermata nera E' l'alpha di un CanvasGroup e che il cadavere non si muove - ma il legame
    /// camera/cadavere e' provato per DEDUZIONE (una sola `AddCameraTarget` in tutto l'assembly, un solo
    /// chiamante dentro il load) e **nessuna riga di log ha mai nominato la camera**. Questa probe
    /// chiude tre domande in UNA sessione, insieme al test di M1 che e' gia' in attesa:
    ///
    ///   1. `cam=` e `target=`  -> la camera insegue davvero il cadavere? (`target` = il punto che
    ///      ProCamera2D insegue; `posMia` = il transform del Penitente, cioe' il bersaglio legato a
    ///      `CameraManager.cs:105`). Se `target` resta addosso a `posMia` mentre il mondo gira, la
    ///      risposta e' si'.
    ///   2. `figlioDiGameplayUI=` -> la schermata di morte sta SOTTO `Core.UI.GameplayUI`? E' il fork
    ///      dichiarato in §7.2 dello studio: `Cinematics.CamCinematicMode` SPEGNE `GameplayUI` per tutta
    ///      la cutscene (`Cinematics.cs:272`). Se la schermata fosse li' sotto, durante la cutscene del
    ///      boss sparisce DA SOLA; se e' un widget fratello, resta sopra il video - ed e' esattamente la
    ///      differenza fra "la cutscene la vedi grazie a noi" e "la vedevi gia'".
    ///   3. `normalAlpha=` -> da DOVE parte l'alpha della schermata: e' la misura che serve al Passo 1.
    ///
    /// DOVE GIRA. In `Multiplayer.OnLateUpdate`, il ciclo per frame che chiama gia' `CutsceneProbe.Tick`:
    /// **zero patch Harmony nuove** (i bersagli restano 263) e **nessun MonoBehaviour nuovo**.
    ///
    /// QUANTO SCRIVE. Solo mentre `PlayerDeathSync.IsLocalPlayerDead()` e' vero: una riga all'ingresso
    /// nella finestra di morte, poi una ogni `SAMPLE_INTERVAL` secondi (~12 righe per una finestra di
    /// 24 s), piu' una riga di chiusura. **Bounded, e solo da morti**: e' una misura, non uno spam.
    ///
    /// ⚠️ TRE TRAPPOLE, tutte rispettate qui:
    ///   - il widget si trova con **`Resources.FindObjectsOfTypeAll<DeadScreenWidget>()`**: in Unity
    ///     2017.4 `FindObjectsOfType<T>(bool)` **non esiste** (verificato per riflessione sul
    ///     `UnityEngine.CoreModule.dll` del pacchetto `Blasphemous.GameLibs 4.0.67`: gli unici overload
    ///     sono `(Type)` e `<T>()`) e `FindObjectsOfType<T>()` **salta gli oggetti disattivati** = no-op
    ///     silenzioso. `Resources.FindObjectsOfTypeAll<T>()` restituisce anche gli inattivi - la stessa
    ///     famiglia che il gioco usa a `CameraManager.cs:75`.
    ///   - `GetComponentsInChildren<T>(bool)` invece **esiste** (verificato) ed e' gia' usato dal progetto.
    ///   - ogni lettura ha il suo `null`-check e ogni punto di ingresso il suo `try/catch`: una probe di
    ///     solo log non deve poter lanciare dentro `OnLateUpdate` (e' il guasto da 2934 errori).
    /// </summary>
    internal static class DeathScreenProbe
    {
        /// <summary>
        /// Marker delle righe di MISURA. Distinto dal marker della trasparenza (`DeathScreenTransparency`)
        /// di proposito: "non ho trovato niente" e "l'ho spento" devono restare separabili in un log.
        /// </summary>
        private const string MARK = "[DeathScreen] misura";

        /// <summary>L'ultima riga di difesa, come in `CutsceneProbe`.</summary>
        private const string MARK_FAIL = "[DeathScreen ERR]";

        /// <summary>
        /// Secondi fra due campioni mentre si e' morti. Due, come il report di `PerformanceProbe`:
        /// abbastanza fitto per vedere se la camera segue (una finestra di morte misurata e' ~24 s, quindi
        /// ~12 campioni) e abbastanza rado da non affogare il registro.
        /// </summary>
        private const float SAMPLE_INTERVAL = 2f;

        private static bool _wasDead;
        private static float _nextSample;
        private static int _samples;

        /// <summary>
        /// Un campione per frame, **solo da morti**. Chiamata da `Multiplayer.OnLateUpdate`.
        /// Il primo giro dopo la morte scrive subito (e' il campione che dice da dove si parte), i
        /// successivi a cadenza; all'uscita dalla finestra scrive una riga di chiusura col conteggio.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (!dead)
                {
                    if (_wasDead)
                    {
                        _wasDead = false;
                        ModLog.Info(MARK + " FINE finestra di morte campioni=" + _samples);
                        _samples = 0;
                    }
                    return;
                }

                float now = Time.unscaledTime;
                if (_wasDead && now < _nextSample)
                    return;

                bool primo = !_wasDead;
                _wasDead = true;
                _nextSample = now + SAMPLE_INTERVAL;
                _samples++;

                ModLog.Info(Sample(primo, now));
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// Costruisce la riga. OGNI lettura e' isolata: se la camera o ProCamera2D non fossero
        /// disponibili in quel momento, il campo dice `errore` e **la riga esce lo stesso** - un campo
        /// mancante non deve poter cancellare la misura degli altri tre.
        /// </summary>
        private static string Sample(bool primo, float now)
        {
            Camera main = null;
            string cam = "n/d";
            try
            {
                main = Camera.main;
                if (main != null)
                    cam = V(main.transform.position);
            }
            catch (Exception)
            {
                cam = "errore";
            }

            string target = "n/d";
            string dist = "n/d";
            try
            {
                // `ProCamera2D.Instance` (public static) e `CameraTargetPosition` (public Vector3):
                // quest'ultima e' la stessa proprieta' che il GIOCO legge a `CameraManager.cs:128`.
                ProCamera2D pro = ProCamera2D.Instance;
                if (pro != null)
                {
                    Vector3 targetPosition = pro.CameraTargetPosition;
                    target = V(targetPosition);
                    if (main != null)
                        dist = F(Vector3.Distance(main.transform.position, targetPosition));
                }
            }
            catch (Exception)
            {
                target = "errore";
            }

            string posMia = "n/d";
            try
            {
                if (Core.Logic != null && Core.Logic.Penitent != null)
                    posMia = V(Core.Logic.Penitent.transform.position);
            }
            catch (Exception)
            {
                posMia = "errore";
            }

            int trovati = 0;
            DeadScreenWidget widget = null;
            string normalAlpha = "n/d";
            string figlio = "n/d";
            try
            {
                widget = FindWidget(out trovati);
                if (widget != null && widget.NormalGroup != null)
                {
                    normalAlpha = F(widget.NormalGroup.alpha);

                    // Il fork di §7.2 dello studio, in un campo solo: la schermata sparisce da sola
                    // durante la cutscene del boss (se sta sotto GameplayUI) o resta sopra il video?
                    if (Core.UI != null && Core.UI.GameplayUI != null)
                        figlio = widget.NormalGroup.transform.IsChildOf(Core.UI.GameplayUI.transform) ? "si" : "no";
                }
            }
            catch (Exception)
            {
                normalAlpha = "errore";
            }

            return MARK + (primo ? " INIZIO" : "") + " t=" + F(now) + " campione=" + _samples +
                   " cam=" + cam + " target=" + target + " posMia=" + posMia + " distCamTarget=" + dist +
                   " normalAlpha=" + normalAlpha + " figlioDiGameplayUI=" + figlio +
                   " widget=" + trovati;
        }

        /// <summary>
        /// Trova il widget della schermata di morte, e **dice quanti ne ha trovati**.
        ///
        /// `Resources.FindObjectsOfTypeAll<T>()` e non `FindObjectsOfType<T>()`: vedi il commento di
        /// classe. Il filtro `NormalGroup != null` scarta gli oggetti che non sono la schermata
        /// (l'API restituisce anche gli asset), e fra piu' candidati si preferisce quello ATTIVO nella
        /// gerarchia - il widget vivo e' attivo da sempre (`Awake` si iscrive a `SpawnManager.OnPlayerSpawn`),
        /// e' reso invisibile solo dagli alpha.
        ///
        /// `found` e' un conteggio, non un booleano: "trovato=0" e "trovato=2" sono due guasti diversi e
        /// il log deve poterli distinguere.
        /// </summary>
        internal static DeadScreenWidget FindWidget(out int found)
        {
            found = 0;

            DeadScreenWidget chosen = null;
            DeadScreenWidget fallback = null;

            DeadScreenWidget[] all = Resources.FindObjectsOfTypeAll<DeadScreenWidget>();
            if (all == null)
                return null;

            foreach (DeadScreenWidget candidate in all)
            {
                if (candidate == null || candidate.NormalGroup == null)
                    continue;

                found++;

                if (fallback == null)
                    fallback = candidate;

                if (chosen == null && candidate.gameObject.activeInHierarchy)
                    chosen = candidate;
            }

            // `!= null` e non `??`: su UnityEngine.Object l'operatore di Unity vede anche l'oggetto
            // DISTRUTTO, mentre `??` guarda solo il riferimento - e qui la differenza e' un widget morto
            // restituito come se fosse vivo.
            return chosen != null ? chosen : fallback;
        }

        /// <summary>Un vettore in forma compatta e leggibile, con i numeri in formato INVARIANTE.</summary>
        internal static string V(Vector3 v)
        {
            return "(" + F(v.x) + "," + F(v.y) + "," + F(v.z) + ")";
        }

        /// <summary>
        /// Formato dei numeri FISSO (invariante): la cultura di questa macchina puo' essere italiana e
        /// scrivere `0,5`. La chiave di lettura del registro e' l'ordine e il tempo, e un separatore che
        /// cambia renderebbe i numeri ambigui da leggere e da cercare.
        /// </summary>
        internal static string F(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// L'ultima riga di difesa: se fallisse anche `ModLog.Error`, l'eccezione uscirebbe dal punto di
        /// ingresso e arriverebbe dentro il gioco. E' l'unico danno che una probe di solo log puo' fare.
        /// </summary>
        internal static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK_FAIL + " in " + where + " (nessun effetto sul gioco): " + e.Message);
            }
            catch
            {
            }
        }
    }
}
