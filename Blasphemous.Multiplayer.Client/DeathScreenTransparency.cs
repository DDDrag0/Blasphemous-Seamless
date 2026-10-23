using Blasphemous.ModdingAPI;
using Gameplay.UI.Others.Buttons;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// PASSO 1 — LA SCHERMATA DI MORTE TRASPARENTE, cosi' il morto VEDE.
    ///
    /// COSA FA, IN UNA RIGA. Quando il giocatore locale e' morto, azzera l'alpha dei **`Graphic` FIGLI di
    /// `NormalGroup`** (`Image`/`RawImage`/`Text`) - cioe' dello sfondo/disegno della schermata di morte.
    /// Il PROMPT resta visibile, perche' vive su un ALTRO `CanvasGroup` (`ContinueGroup`) e non si tocca.
    ///
    /// PERCHE' SUI FIGLI E NON SUL `CanvasGroup` (e' il punto di tutta la modifica).
    /// `DeadScreenWidget.OnDeadAction` fa `NormalGroup.DOFade(1f, 2.15f)` (`DeadScreenWidget.cs:82`):
    /// **il tween riscrive `CanvasGroup.alpha`**, quindi una scrittura una-tantum su quell'alpha verrebbe
    /// annullata in 2,15 s (servirebbe `DOKill` o una riscrittura per frame, cioe' una lotta di frame).
    /// L'alpha del `Graphic` invece **non e' animato da `DOFade`**, e il `CanvasGroup` lo **moltiplica**:
    /// `color.a = 0` per 1 = 0. ⇒ **nessuna lotta col tween, nessun DOTween da referenziare.**
    ///
    /// PERCHE' `NormalGroup` E BASTA (e il prompt non c'entra).
    /// `NormalGroup`, `BossRushGroup`, `DemakeGroup` e `ContinueGroup` sono quattro `CanvasGroup` distinti
    /// e **il prompt non puo' essere annidato nello sfondo**: in Demake (`OnDeadDemake` :165-186) il gioco
    /// accende `DemakeGroup` E `ContinueGroup` **senza mai toccare `NormalGroup`**, che resta a 0 da
    /// `HideAll`. Se `ContinueGroup` stesse dentro `NormalGroup` sarebbe moltiplicato per alpha 0 e il
    /// prompt in Demake non potrebbe mai apparire. ⇒ **rendere trasparente `NormalGroup` non tocca il prompt**.
    ///
    /// ⭐⭐ SOLO IN CO-OP, E SI PUO' TOGLIERE (richiesta dell'utente: "la schermata di morte torna ad
    /// essere normale in single player, deve essere trasparente solo quando ci si connette a un server").
    /// Il cancello e' lo **stesso predicato della Colpa** — `CoopGate.IsMultiplayerActive()`, cioe' il
    /// socket verso il server, host o peer — e sta in cima a `Tick()`, prima di qualunque lettura:
    /// in single player questo file **non scrive niente**.
    ///
    /// E siccome "non scrivere" non basta quando una sessione co-op ha GIA' azzerato gli alpha (la
    /// trasparenza e' permanente sull'istanza di scena: sotto c'e' la spiegazione del perche'), ora c'e'
    /// un **ripristino**: la prima volta che si azzera un `Graphic` si registra il suo alpha VERO, e
    /// quando il cancello si chiude (connessione caduta, o partita single player) quei valori tornano.
    /// ⇒ "in single player la schermata e' quella del gioco" e' vero **anche dopo** una sessione co-op
    /// nello stesso processo. L'RGB non si tocca mai: si salva e si rimette **solo** `a`.
    ///
    /// PERCHE' NON C'ERA UN RIPRISTINO, E PERCHE' ADESSO C'E'. Mentre si gioca la schermata e' invisibile
    /// **comunque**, perche' `HideAll()` mette il `CanvasGroup` a 0 (`Awake` :57 e dopo ogni respawn :94)
    /// ⇒ azzerare l'alpha dei figli **non ha alcun effetto visibile fuori dalla morte**, e non c'era nulla
    /// da rimettere al suo posto. Con la guardia, pero', il "fuori dalla morte" e' diventato uno STATO
    /// OSSERVABILE (single player) e va riportato al valore del gioco. Il ripristino e' esatto, non
    /// "rimetto 1": si registra l'alpha che c'era.
    ///
    /// IL RESTO DEL DISEGNO NON CAMBIA. `NormalGroup` e' usato **solo** dal percorso di morte normale
    /// (`grep`: compare solo in `DeadScreenWidget.cs`, solo via `alpha`); BossRush e Demake usano i LORO
    /// gruppi; il prompt vive su `ContinueGroup` e non si tocca. L'auto-riparazione resta sul fronte di
    /// salita di `PlayerDeathSync.IsLocalPlayerDead()` + una riapplicazione al secondo.
    ///
    /// L'AUTO-RIPARAZIONE (scelta e dichiarata). L'applicazione avviene sul **fronte di salita** di
    /// `PlayerDeathSync.IsLocalPlayerDead()`, e poi si **riapplica ogni `REAPPLY_INTERVAL`** finche' si e'
    /// morti: se l'oggetto venisse ricreato (o un `Graphic` aggiunto a runtime) si ripara da se'. Non e'
    /// "ogni frame" di proposito: `GetComponentsInChildren` alloca un array a ogni chiamata, e un'allocazione
    /// per frame per 24 s e' esattamente il costo che `PerformanceProbe` misura. Un secondo di ritardo su una
    /// finestra che dura ~24 s non si vede.
    /// La riga di riapplicazione si scrive **solo quando cambia qualcosa**: un log identico ogni secondo
    /// sarebbe spam.
    ///
    /// DOVE GIRA. `Multiplayer.OnLateUpdate`, lo stesso ciclo per frame di `CutsceneProbe`/`DeathScreenProbe`:
    /// **zero patch Harmony nuove** (i bersagli restano 263), nessun `MonoBehaviour` nuovo, e `LateUpdate`
    /// ⇒ **dopo** l'Update di DOTween.
    ///
    /// I DIVIETI, riga per riga.
    ///   - ⛔ `Core.Input.SetBlocker`: **zero occorrenze** in questo file. Il blocker `DEAD_SCREEN` resta del
    ///     gioco: lo spegne `DeadScreenWidget.OnPenitentReady` (:101).
    ///   - ⛔ `TimeScale`: non citato.
    ///   - ⛔ la camera: non citata (questo intervento **non ne ha bisogno**: e' il suo pregio).
    ///   - ⛔ il `Penitent` dell'altro: non citato.
    ///   - ⛔ `SetActive(false)`: **zero occorrenze**. Si scrive solo `Graphic.color`.
    ///   - ⛔ `BossRushGroup`/`DemakeGroup`: **zero occorrenze** - romperebbero gli altri due percorsi di morte.
    ///   - ✅ e il respawn NON e' in pericolo: `Respawn()` sta nel callback del fade (`:91-96`) e **non legge
    ///     nessun alpha**; l'uscita dalla schermata e' `while (!Input.anyKey)` (`:87-90`).
    /// </summary>
    internal static class DeathScreenTransparency
    {
        /// <summary>
        /// Marker di ESITO. E' obbligatorio e sta su OGNI riga di questo file, perche' senza di lui
        /// "non ho trovato il widget", "era gia' spento" e "l'ho spento io" hanno lo stesso aspetto in un log.
        /// </summary>
        private const string MARK = "[DeathScreen] trasparenza";

        /// <summary>Cadenza dell'auto-riparazione, in secondi. Vedi il commento di classe.</summary>
        private const float REAPPLY_INTERVAL = 1f;

        // `_dead` e' la finestra di morte vista da noi, e serve SOLO a distinguere il primo giro
        // (fronte di salita, che logga sempre) dai successivi (cadenza, che logga solo se cambia qualcosa).
        private static bool _dead;
        private static float _nextApply;

        // Una sola riga d'errore per finestra di morte: se il widget non si trova, non lo si ripete ogni
        // secondo per 24 s.
        private static bool _missingReported;

        // ⭐ GLI ALPHA VERI, per poter TORNARE INDIETRO. Da quando la trasparenza vale solo in co-op, la
        // schermata deve tornare quella del gioco quando il cancello si chiude: si registra `a` PRIMA di
        // azzerarlo (l'RGB non basta, l'alpha non si indovina) e lo si rimette. Due liste parallele invece
        // di un `Dictionary` perche' gli elementi sono pochi e l'ordine non conta: meno codice, meno cose
        // che possono lanciare. Si svuotano al ripristino.
        private static readonly List<Graphic> _zeroedGraphics = new List<Graphic>();
        private static readonly List<float> _zeroedAlphas = new List<float>();

        /// <summary>
        /// Un giro per frame. Chiamata da `Multiplayer.OnLateUpdate`, subito dopo `DeathScreenProbe.Tick()`.
        /// Il `try/catch` non e' decorativo: un'eccezione qui uscirebbe da un punto di ingresso del ciclo
        /// della mod - ed e' il guasto che una volta ha prodotto 2934 errori.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                // ⭐ IL CANCELLO DEL CO-OP, prima di qualunque lettura: "trasparente solo quando ci si
                //   connette a un server" (host o peer, `CoopGate`). In single player: nessuna scrittura e,
                //   se una sessione co-op aveva gia' azzerato gli alpha, RIPRISTINO ai valori veri.
                if (!CoopGate.IsMultiplayerActive())
                {
                    if (_dead)
                    {
                        _dead = false;
                        _missingReported = false;
                    }

                    Restore("single player: nessun server");
                    return;
                }

                bool dead = PlayerDeathSync.IsLocalPlayerDead();

                if (!dead)
                {
                    if (_dead)
                    {
                        _dead = false;
                        _missingReported = false;
                        ModLog.Info(MARK + ": fine finestra di morte, nessun ripristino " +
                                    "(lo sfondo resta trasparente: non c'e' nulla da rimettere)");
                    }
                    return;
                }

                float now = Time.unscaledTime;
                bool first = !_dead;
                _dead = true;

                if (!first && now < _nextApply)
                    return;

                _nextApply = now + REAPPLY_INTERVAL;
                Apply(first);
            }
            catch (Exception e)
            {
                DeathScreenProbe.Fail("DeathScreenTransparency.Tick", e);
            }
        }

        /// <summary>
        /// Rimette gli alpha **veri** dei `Graphic` che questa mod ha azzerato, e svuota il registro.
        /// Idempotente: la seconda chiamata non ha niente da rimettere e non scrive una riga di log.
        ///
        /// ⚠️ Perche' non basta "non applicare": l'azzeramento e' **permanente sull'istanza di scena**
        /// (`Tick()` lo scrive e non lo rimette), quindi chi ha giocato in co-op e poi e' rimasto senza
        /// server si troverebbe la schermata sbiadita anche in single player. E quel caso l'utente lo ha
        /// chiesto esplicitamente: "in single player la schermata torna normale". Si controlla
        /// `graphic == null` (semantica Unity: un oggetto distrutto e' null) perche' il registro puo'
        /// sopravvivere a un cambio scena.
        /// </summary>
        private static void Restore(string why)
        {
            try
            {
                if (_zeroedGraphics.Count == 0)
                    return;

                int restored = 0;

                for (int i = 0; i < _zeroedGraphics.Count; i++)
                {
                    Graphic graphic = _zeroedGraphics[i];
                    if (graphic == null)
                        continue;

                    Color color = graphic.color;
                    graphic.color = new Color(color.r, color.g, color.b, _zeroedAlphas[i]);
                    restored++;
                }

                _zeroedGraphics.Clear();
                _zeroedAlphas.Clear();

                if (restored > 0)
                {
                    ModLog.Info(MARK + " RIPRISTINATA (" + why + ") alphaRimessi=" +
                                restored.ToString() + " · la schermata di morte e' di nuovo quella del gioco");
                }
            }
            catch (Exception e)
            {
                // Se il ripristino fallisce si svuota comunque il registro: riprovare ogni frame su un
                // oggetto rotto sarebbe solo spam. La schermata resta com'e' (invisibile fuori dalla morte).
                _zeroedGraphics.Clear();
                _zeroedAlphas.Clear();
                ModLog.Error(MARK + " ripristino NO (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· la schermata resta com'e', niente di rotto");
            }
        }

        /// <summary>
        /// Azzera l'alpha dei `Graphic` di `NormalGroup`. Idempotente: riscrivere 0 su 0 non cambia niente,
        /// e per questo puo' essere richiamata a cadenza senza stato da sbagliare.
        ///
        /// Il widget si cerca **a ogni applicazione**, non si tiene in cache per sempre: se la scena viene
        /// ricaricata il widget vecchio viene distrutto, e una cache stantia renderebbe la modifica un
        /// no-op silenzioso proprio dopo un respawn.
        /// </summary>
        private static void Apply(bool first)
        {
            int found;
            DeadScreenWidget widget = DeathScreenProbe.FindWidget(out found);

            if (found > 1 && first)
            {
                // Non e' un guasto: e' un fatto che il log deve dichiarare, perche' "uso il primo" puo'
                // essere sbagliato se il primo non fosse quello vivo.
                ModLog.Info(MARK + ": trovato=" + found + " widget con NormalGroup - uso il primo attivo " +
                            "nella gerarchia (dichiarato, non silenzioso)");
            }

            if (widget == null || widget.NormalGroup == null)
            {
                if (!_missingReported)
                {
                    _missingReported = true;
                    ModLog.Error(MARK + " NON applicata motivo=widget-non-trovato trovato=" + found +
                                 " (Resources.FindObjectsOfTypeAll<DeadScreenWidget>() con NormalGroup != null)");
                }
                return;
            }

            _missingReported = false;

            // `GetComponentsInChildren<Graphic>(true)`: il `(bool)` esiste in questa Unity (verificato per
            // riflessione su UnityEngine.CoreModule.dll) ed e' gia' usato dal progetto
            // (EnemySyncManager.cs:2244, OtherPlayerScript.cs:293, ...). `true` perche' la schermata puo'
            // avere figli disattivati che diventano attivi piu' tardi.
            Graphic[] graphics = widget.NormalGroup.GetComponentsInChildren<Graphic>(true);
            int children = graphics != null ? graphics.Length : 0;

            int zeroed = 0;
            for (int i = 0; i < children; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null)
                    continue;

                Color color = graphic.color;
                if (color.a <= 0f)
                    continue;

                // SOLO l'alpha. L'RGB resta: e il valore ORIGINALE di `a` si registra PRIMA di azzerarlo,
                // perche' e' quello che `Restore()` rimette quando il cancello del co-op si chiude. Se lo
                // stesso Graphic fosse gia' nel registro non si riscrive: il primo valore e' quello vero.
                if (!_zeroedGraphics.Contains(graphic))
                {
                    _zeroedGraphics.Add(graphic);
                    _zeroedAlphas.Add(color.a);
                }

                graphic.color = new Color(color.r, color.g, color.b, 0f);
                zeroed++;
            }

            // L'alpha del CanvasGroup NON si tocca, ma si LEGGE: e' il numero che dice se il DOFade sta
            // facendo il suo lavoro (parte da 0 e va a 1 in 2,15 s) - cioe' se la schermata si sta
            // accendendo mentre i suoi figli restano invisibili. E' la prova che le due cose sono separate.
            float groupAlpha = widget.NormalGroup.alpha;

            if (first)
            {
                ModLog.Info(MARK + " applicata: trovato=" + found + " figli=" + children +
                            " azzerati=" + zeroed + " alphaGruppo=" + DeathScreenProbe.F(groupAlpha) +
                            " applicato=" + (children > 0 ? "si" : "no") +
                            (zeroed == 0 && children > 0 ? " (gli alpha dei figli erano gia' tutti 0)" : "") +
                            (children == 0 ? " motivo=nessun-graphic-figlio" : "") +
                            " · il prompt e' su ContinueGroup e NON e' stato toccato");

                if (children == 0)
                {
                    ModLog.Error(MARK + " NON applicata motivo=NormalGroup-senza-Graphic figli=0 " +
                                 "applicato=no (lo sfondo non e' un Graphic figlio di NormalGroup)");
                }
            }
            else if (zeroed > 0)
            {
                // Solo se e' cambiato qualcosa: un log identico ogni secondo sarebbe spam.
                ModLog.Info(MARK + " riapplicata (auto-riparante) azzerati=" + zeroed +
                            " figli=" + children + " alphaGruppo=" + DeathScreenProbe.F(groupAlpha));
            }
        }
    }
}
