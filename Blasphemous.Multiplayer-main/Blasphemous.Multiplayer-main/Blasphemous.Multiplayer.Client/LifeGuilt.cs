using System;
using System.Globalization;
using System.IO;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using HarmonyLib;
using Tools.Level.Interactables;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// LA COLPA SULLA VITA — una penalita' NUOVA, sul BINARIO SEPARATO della vita, che si azzera SOLO
    /// al falo'.
    ///
    /// PERCHE' NON TOCCA LA COLPA NATIA. La Colpa del gioco e' un `MaxFactor` sull'attributo `Fervour`
    /// (`GuiltManager.cs:98`, `:243`), e `Life : VariableAttribute` (`Life.cs:5`) e' lo STESSO TIPO di
    /// attributo: `Stats.Life.MaxFactor` esiste da sempre, e' pubblico, e **nessuno lo ha mai scritto**
    /// in tutto l'assembly (6 occorrenze testuali di `MaxFactor = `, 2 scritture vere, entrambe su
    /// Fervour). Due fattori su due attributi distinti: la collisione e' impossibile per costruzione,
    /// non per disciplina. Il Fervour, `GuiltManager`, `ResetGuilt` e la config della colpa restano
    /// INTATTI: qui nessuno li nomina.
    ///
    /// LA DESIGN, che non si interpreta:
    ///   · **TRE SCALINI FISSI** — 0 morti => 1.0 · 1 => 0.8 · 2 => 0.6 · 3 o piu' => 0.4. Niente
    ///     curva: il gioco ne ha una (`scratch\DIAGNOSI-COLPA-CURVA.md` la misura: −17%, −9,7%, −3,3%,
    ///     −5%), la nostra e' piu' semplice e voluta. Il **terzo scalino e' l'ultimo, fermato nel
    ///     codice**: `Factor()` non scende sotto 0.4 per nessun numero di morti.
    ///   · **IL RESET** — si azzera SOLO sedendosi a un `PrieDieu`, e in nessun altro modo. NON il
    ///     frammento, NON il Confessore: quella e' la colpa nativa, che resta com'e'.
    ///   · **IL CONTATORE** — le morti da falo' a falo', SALVATO in un file della mod (vedi §SALVATAGGIO),
    ///     quindi chiudere il gioco non e' una via d'uscita.
    ///   · **IN SINGLE PLAYER** — il mod e' co-op: senza peer non c'e' nulla, e non serve un caso a
    ///     parte. Non c'e' nessun cancello sulla connessione, esattamente come la misura `[Colpa]`.
    ///
    /// I TRE GANCI (due esistevano gia'):
    ///   ① LA MORTE — il prefix GIA' PRESENTE su `Penitent.OnUpdate` (`PlayerDeathSync.cs`), con la
    ///     guardia del gioco `Status.Dead` E `!DeathEventLaunched` => una volta per morte. Una riga:
    ///     `RegisterDeath()`.
    ///   ② LO SPAWN — il postfix GIA' PRESENTE su `SpawnManager.CreatePlayer` => una riga:
    ///     `ApplyOnSpawn()`. ⚠️ **E' UN REQUISITO, NON UN'OTTIMIZZAZIONE**: `MaxFactor` vive
    ///     sull'ISTANZA dell'attributo, che rinasce a ogni `Instantiate` (`SpawnManager.cs:489`) =>
    ///     senza, la colpa sparirebbe al primo respawn. L'ordine e' gia' quello giusto: `OnPlayerSpawn`
    ///     (dove `GuiltManager.OnPenitentReady` scrive la colpa nativa) gira DENTRO `CreatePlayer`,
    ///     quindi il postfix viene dopo.
    ///   ③ IL FALO' — **UNA PATCH NUOVA** (269 -> 270), prefix su `PrieDieu.ShallowActivationLogic`
    ///     (privato, `PrieDieu.cs:222`). ⚠️ **PERCHE' PREFIX**: il `Life.SetToCurrentMax()` del gioco
    ///     sta DOPO (`:225`) => un prefisso fa riempire al massimo PIENO, un postfisso lascerebbe il
    ///     giocatore mezzo vuoto. ⚠️ **PERCHE' QUEL METODO**: la replica non lo chiama MAI (chiama
    ///     `UsePrieDieu`, `EnemySyncManager.cs:3414`) => il reset e' LOCALE: chi si siede lo ha, chi no
    ///     no. E' la regola del progetto, "everyone gets the respawn, only the sitter gets the
    ///     comfort" (`EnemySyncManager.cs:3364-3367`).
    ///
    /// IL PAVIMENTO E' DENTRO `Factor()`, e non e' negoziabile: `set_MaxFactor` chiama subito
    /// `CalculateValue`, che taglia la vita corrente al nuovo tetto (`VariableAttribute.cs:83-91`), e
    /// `Entity.Dead` e' "Life.Current minore o uguale a 0" (`Entity.cs:71`) => un fattore 0 UCCIDEREBBE
    /// all'istante.
    /// Il nostro minimo e' 0.4, scritto nella stessa funzione che calcola il fattore, cosi' nessun ramo
    /// futuro puo' scavalcarlo.
    ///
    /// ⭐⭐ DUE GUARDIE, UNA FUNZIONE SOLA (`SuppressionReason()`), e la richiesta e' dell'utente:
    ///   · **SINGLE PLAYER** — "la Colpa deve attivarsi solo quando ci si connette a un server; in
    ///     single player il debuff deve essere disattivato e puo' curarsi". Il predicato e' il SOCKET
    ///     (`CoopGate.IsMultiplayerActive()`, `NetworkManager.IsConnected`): host o peer, appena la
    ///     connessione e' viva la Colpa torna. In single player: `Factor()` = 1, **nessuna** scrittura
    ///     lesiva, **nessuna** notifica, e **il contatore non cresce** (una morte in SP non e' una morte
    ///     in co-op).
    ///   · **PE02 / DEMAKE** — le due modalita' in cui la vita e' fatta di STOCK ricavati da
    ///     `Life.CurrentMax` (`PlayerHealthPE02`/`PlayerHealthDemake`: `CeilToInt(Life.CurrentMax / 30)`),
    ///     quindi un `MaxFactor` ridotto cambierebbe il NUMERO di sfere: una penalita' molto piu' severa
    ///     di quella voluta. Il gioco spegne la sua colpa in `DEMAKE` (`GuiltManager.cs:93`) e qui si fa
    ///     lo stesso; PE02 si riconosce dalla penitenza ATTIVA (`GetCurrentPenitence().Id == "PE02"`).
    ///     ⚠️ `BOSS_RUSH` **non** e' in lista ed e' voluto: la colpa nativa si spegne anche li', ma in
    ///     Boss Rush la vita e' la barra normale, non stock ⇒ li' la penalita' deve funzionare.
    ///
    /// TUTTO E' DENTRO try/catch: una colpa non deve mai rompere una partita. Nessun `Kill`, nessun
    /// `Damage`, nessun `SetHealth`, nessun `Time.timeScale`, nessuna rete, nessun protocollo.
    /// </summary>
    internal static class LifeGuilt
    {
        /// <summary>Il marker di ogni riga: si conta da solo nel log.</summary>
        internal const string MARK = "[ColpaVita]";

        // ---- I TRE SCALINI, FISSI, e il quarto stato e' "3 o piu'".
        private const float FACTOR_NO_GUILT = 1f;
        private const float FACTOR_ONE_DEATH = 0.8f;
        private const float FACTOR_TWO_DEATHS = 0.6f;
        private const float FACTOR_LAST_STEP = 0.4f;

        /// <summary>Lo scalino massimo: oltre, il fattore non scende piu'.</summary>
        private const int LAST_STEP = 3;

        /// <summary>
        /// Il tetto del contatore. Non e' il tetto della PENALITA' (quello e' `LAST_STEP`): e' il tetto
        /// del NUMERO, perche' un intero che cresce per sempre e' solo un modo per avere un file assurdo.
        /// </summary>
        private const int MAX_DEATHS = 999;

        // ---- IL SALVATAGGIO. La mod non scriveva NIENTE in un file proprio: i file che legge vivono
        //      in `Modding/data/Multiplayer/` (`attacks.json`, `defenses.json`, `offenses.json`, letti
        //      con `FileHandler.LoadDataAsJson`), e le stringhe di localizzazione in
        //      `Modding/localization/`. La cartella giusta e' quindi `Modding/data/Multiplayer/`, e il
        //      formato piu' semplice che regge un intero + un'istruzione per chi apre il file e' TESTO
        //      PURO con una riga di commento. Un JSON avrebbe richiesto un serializzatore per un solo
        //      numero, e — peggio — un file corrotto avrebbe lanciato dentro il deserializzatore.
        private const string FILE_NAME = "life-guilt.txt";
        private const string FILE_KEY = "deaths=";

        // ---- lo stato. `_deaths` e' l'unica verita'; il file ne e' la copia su disco.
        private static int _deaths;
        private static int _announcedStep;
        private static bool _loaded;
        private static string _path;

        // ---- il FRONTE del co-op, letto una volta per frame da `Tick()`: `_gateKnown`/`_gateMultiplayer`
        //      sono l'ultimo stato visto, e il fronte e' il cambio. Non e' una cache del predicato (che
        //      costa due confronti): e' lo stato che serve per accorgersi che la connessione e' caduta.
        private static bool _gateKnown;
        private static bool _gateMultiplayer;

        // ============================================================================================
        // ⭐ LE DUE GUARDIE — una funzione sola, un motivo solo.
        // ============================================================================================

        /// <summary>
        /// Il motivo per cui la Colpa **non si applica adesso**, oppure `null` se si applica. Una funzione
        /// sola perche' `Factor()`, `ApplyOnSpawn()`, `RegisterDeath()`, `Notify()` e `Tick()` non possano
        /// rispondere cose diverse.
        ///
        /// ① **SINGLE PLAYER** — l'utente: "deve attivarsi solo quando ci si connette a un server... in
        ///    single player il debuff deve essere disattivato e puo' curarsi". Il predicato e' il SOCKET,
        ///    e vale **host o peer** (in entrambi i casi il client e' connesso al server). Dettagli e
        ///    caso "connesso senza altri giocatori" in `CoopGate.cs`.
        ///
        /// ② **PE02 / DEMAKE** — le due modalita' a STOCK, dove `Life.CurrentMax` decide il NUMERO di
        ///    sfere:
        ///      · **Demake**: esattamente la condizione con cui il gioco spegne la sua colpa nativa
        ///        (`GuiltManager.cs:93`: `!BOSS_RUSH && !DEMAKE`) — qui la stessa meta' che ci riguarda.
        ///      · **PE02**: la penitenza ATTIVA (`Core.PenitenceManager.GetCurrentPenitence()`, `Id`
        ///        "PE02"), che e' la stessa cosa che guarda la UI (`GameplayWidget.cs:623`,
        ///        `current is PenitencePE02`).
        ///    ⚠️ **PERCHE' NON `UseStocksOfHealth`** (che pure esiste e sarebbe comodo): quella bandiera
        ///    **confonde le due modalita'** — la accende `PenitencePE02.Activate()` (PE02) **e** l'avvio
        ///    del Demake (`DemakeManager`, `LoadDemakeFadeWhiteCourrutine`) — e viene riscritta a `false`
        ///    solo dal `ResetPersistence()` (`PenitenceManager`), che passa dal caricamento del gioco; in
        ///    piu' e' la bandiera che il gioco usa per **quantizzare il danno**, non per scegliere la UI.
        ///    Le due condizioni qui sopra invece sono **quelle vere**: `IsCurrentMode(DEMAKE)` e' la stessa
        ///    riga della colpa nativa, `GetCurrentPenitence()` e' la stessa cosa che guarda la UI
        ///    (`GameplayWidget.cs:623`). Vedi `REFERTO-COLPA-PE02.md`.
        ///
        /// **IN DUBBIO SI APPLICA**: ogni lettura fallita torna `null`, perche' una guardia che spegne la
        /// Colpa in co-op normale sarebbe peggio del difetto che cura.
        /// </summary>
        private static string SuppressionReason()
        {
            try
            {
                if (!CoopGate.IsMultiplayerActive())
                    return "single player (nessun server)";

                if (Core.GameModeManager != null &&
                    Core.GameModeManager.IsCurrentMode(GameModeManager.GAME_MODES.DEMAKE))
                {
                    return "modalita' Demake";
                }

                if (Core.PenitenceManager != null)
                {
                    var penitence = Core.PenitenceManager.GetCurrentPenitence();
                    if (penitence != null && string.Equals(penitence.Id, "PE02", StringComparison.Ordinal))
                        return "modalita' PE02";
                }
            }
            catch (Exception)
            {
                // Vedi sopra: in caso di dubbio, si applica.
            }

            return null;
        }

        // ============================================================================================
        // IL FATTORE — una sola funzione, i tre scalini dentro, il pavimento dentro.
        // ============================================================================================

        /// <summary>
        /// Il fattore della vita massima, dai tre scalini. **Legge il contatore**, e legge il contatore
        /// significa leggere il file la prima volta (vedi `EnsureLoaded`).
        ///
        /// ⭐ Le due guardie stanno QUI, in cima, e non solo nei chiamanti: `Factor()` e' l'unico posto in
        /// cui nasce un numero diverso da 1, quindi nessun ramo futuro puo' scavalcarle.
        /// </summary>
        internal static float Factor()
        {
            if (SuppressionReason() != null)
                return FACTOR_NO_GUILT;

            int deaths = Deaths;

            if (deaths <= 0)
                return FACTOR_NO_GUILT;

            if (deaths == 1)
                return FACTOR_ONE_DEATH;

            if (deaths == 2)
                return FACTOR_TWO_DEATHS;

            // 3 o piu': L'ULTIMO SCALINO, fermato qui. Non c'e' un quarto gradino, e non c'e' un
            // `Mathf.Max` esterno da nessun'altra parte: i tre scalini SONO il pavimento.
            return FACTOR_LAST_STEP;
        }

        /// <summary>Le morti contate da falò a falò.</summary>
        internal static int Deaths
        {
            get
            {
                EnsureLoaded();
                return _deaths;
            }
        }

        // ============================================================================================
        // ① LA MORTE — +1, e il fattore si riapplica subito (anche quando cambia, non solo allo spawn).
        // ============================================================================================

        /// <summary>
        /// Una morte in piu'. Chiamata dal prefix GIA' ESISTENTE su `Penitent.OnUpdate`, dentro la guardia
        /// del gioco (`Status.Dead` E `!DeathEventLaunched`) => **una volta sola per morte**, mai a ogni
        /// frame.
        ///
        /// Il contatore si salva SUBITO, e non e' ridondanza: e' la ragione per cui chiudere il gioco non
        /// e' una via d'uscita. Il gioco salva quando vuole lui (al falo', al `RestoreStored`, ai cambi di
        /// scena), e una morte appena avvenuta non e' ancora in nessun salvataggio.
        /// </summary>
        internal static void RegisterDeath()
        {
            try
            {
                // ⭐ LE GUARDIE PRIMA DI TUTTO: dove la Colpa non si applica, una morte **non e' una morte
                //   per la Colpa**. Ne' il fattore ne' il contatore: cosi' la penalita' non si accumula in
                //   single player (richiesta dell'utente) ne' in PE02/Demake (dove sarebbe un debuff
                //   diverso, sulle sfere), e non "esplode" tornando in co-op normale.
                string reason = SuppressionReason();
                if (reason != null)
                {
                    ModLog.Info(MARK + " non applicata: " + reason +
                                " · morte NON contata, la vita non si tocca, nessuna notifica · " +
                                "il contatore riprende quando la Colpa torna attiva " +
                                "(una penalita' non si accumula dove non si applica)");
                    return;
                }

                EnsureLoaded();

                if (_deaths < MAX_DEATHS)
                    _deaths++;

                float factor = Factor();

                // ⚠️ CONCATENAZIONE, NON INTERPOLAZIONE, e non e' gusto: `MARK` e' una costante, quindi
                // `MARK + " morte="` e' una costante a sua volta e finisce nella DLL come UN letterale
                // ("[ColpaVita] morte="). Con `$"{MARK} morte={_deaths}"` il compilatore sceglie
                // `string.Format` e il marker diventa "{0} morte={1}": presente nel sorgente, invisibile
                // a `marker-check.ps1`. E' lo stesso motivo per cui `PauseNotice` scrive `MARK + " ..."`.
                ModLog.Info(MARK + " morte=" + _deaths.ToString(CultureInfo.InvariantCulture) +
                            " fattore=" + Text(factor) +
                            " · gli scalini sono fissi (1 => 0.8, 2 => 0.6, 3+ => 0.4, l'ultimo fermato nel codice)");

                ApplyNow("morte");
                Save("morte");

                // ⭐ LA NOTIFICA, UNA VOLTA PER SCALINO. Confronto di SCALINI e non di fattori: cosi'
                // una riapplicazione (che riscrive lo stesso numero decine di volte) non produce
                // nessuna riga a schermo, e una morte oltre il terzo scalino non ne produce una nuova.
                int step = StepOf(_deaths);
                if (step > _announcedStep)
                {
                    _announcedStep = step;
                    Notify(factor);
                }
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " morte: contatore NON aggiornato (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· la partita continua, la colpa resta quella di prima");
            }
        }

        // ============================================================================================
        // ② LO SPAWN — la riapplicazione. ⚠️ UN REQUISITO: senza, la colpa sparisce al primo respawn.
        // ============================================================================================

        /// <summary>
        /// Riscrive `Life.MaxFactor` sul Penitente appena creato.
        ///
        /// ⚠️ **NON E' UN'OTTIMIZZAZIONE.** `MaxFactor` vive sull'ISTANZA dell'attributo, e l'istanza
        /// rinasce a ogni `Instantiate` (`SpawnManager.cs:489`) con `_maxFactor = 1f`
        /// (`VariableAttribute.cs:12`). Senza questa riga la penalita' durerebbe fino al primo respawn.
        /// Il gioco fa la stessa cosa con la sua colpa, per la stessa ragione
        /// (`GuiltManager.OnPenitentReady`, agganciato a `SpawnManager.OnPlayerSpawn`).
        ///
        /// E' idempotente: `MaxFactor` e' un setter che ricalcola il clamp, scrivere lo stesso valore due
        /// volte non fa danno. `CreatePlayer` gira ANCHE con `createNewInstance == false`
        /// (`SpawnManager.cs:487-494`): anche li' va riscritto, perche' il fattore puo' essere cambiato
        /// mentre il Penitente era in scena (una morte lo cambia).
        /// </summary>
        internal static void ApplyOnSpawn()
        {
            try
            {
                EnsureLoaded();

                string reason = SuppressionReason();
                float factor = Factor();
                var stats = PlayerStats();

                if (stats == null)
                {
                    ModLog.Warn(MARK + " applicata a ogni spawn · stats=non disponibili (Penitent assente): " +
                                "NESSUNA scrittura, contatore=" + _deaths.ToString(CultureInfo.InvariantCulture) +
                                " fattore=" + Text(factor) +
                                (reason == null ? string.Empty : " · MA non applicata: " + reason));
                    return;
                }

                // ⭐ In guardia il valore scritto e' **1.0**: nessuna riduzione, e se un residuo di una
                //   sessione precedente fosse rimasto sull'istanza, questa riga lo AZZERA (e' la richiesta
                //   "in single player il debuff e' disattivato e puo' curarsi": vita piena, si cura).
                stats.Life.MaxFactor = factor;

                if (reason != null)
                {
                    ModLog.Info(MARK + " non applicata: " + reason + " · scritto Life.MaxFactor=" +
                                Text(factor) + " (nessuna riduzione, nessuna notifica) · vita: max=" +
                                F1(stats.Life.CurrentMax) + " final=" + F0(stats.Life.Final) +
                                " current=" + F0(stats.Life.Current) +
                                " · contatore=" + _deaths.ToString(CultureInfo.InvariantCulture));
                    return;
                }

                ModLog.Info(MARK + " applicata a ogni spawn · contatore=" + _deaths.ToString(CultureInfo.InvariantCulture) +
                            " fattore=" + Text(factor) +
                            " · vita: max=" + F1(stats.Life.CurrentMax) +
                            " final=" + F0(stats.Life.Final) +
                            " current=" + F0(stats.Life.Current) +
                            " (final ignora il fattore: e' la LARGHEZZA della barra, PlayerHealth.cs:116)");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " applicata a ogni spawn NO (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· la partita continua senza la penalita' su questo spawn");
            }
        }

        // ============================================================================================
        // ③ IL FALO' — l'UNICO azzeramento. Il prefix gira PRIMA del rifornimento del gioco.
        // ============================================================================================

        /// <summary>
        /// IL RESET: contatore a 0 e fattore a 1. Chiamata dal **prefix** nuovo su
        /// `PrieDieu.ShallowActivationLogic`.
        ///
        /// ⚠️ L'ORDINE E' TUTTO: `ShallowActivationLogic` fa `Life.SetToCurrentMax()` alla riga dopo
        /// (`PrieDieu.cs:225`). Con il fattore gia' a 1, quel rifornimento riempie al massimo PIENO. Con
        /// un postfix il giocatore si alzerebbe dal falo' **mezzo vuoto** pur avendo la colpa perdonata.
        ///
        /// ⚠️ E QUESTO METODO NON VIENE REPLICATO: la replica del falo' chiama `Core.Logic.UsePrieDieu()`
        /// (`EnemySyncManager.cs:3414`), mai `ShallowActivationLogic`. ⇒ il perdono e' di chi si siede.
        /// </summary>
        internal static void ResetAtBonfire()
        {
            try
            {
                EnsureLoaded();

                int before = _deaths;
                _deaths = 0;
                _announcedStep = 0;

                var stats = PlayerStats();
                if (stats != null)
                    stats.Life.MaxFactor = 1f;

                Save("falo'");

                ModLog.Info(MARK + " reset al falo' · contatore 0 · prima=" + before.ToString(CultureInfo.InvariantCulture) +
                            " · fattore=1.0 " +
                            "· ora il Life.SetToCurrentMax() del gioco (PrieDieu.cs:225) riempie al massimo PIENO " +
                            "· la colpa nativa (Fervour) NON e' toccata: quella resta com'e'");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " reset al falo' NO (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· il giocatore si alza con la colpa di prima, niente di rotto");
            }
        }

        // ============================================================================================
        // ③-bis IL FRONTE DEL CO-OP — si accende quando ci si connette, si spegne quando cade.
        // ============================================================================================

        /// <summary>
        /// Un giro per frame (da `Multiplayer.OnLateUpdate`), e fa **una cosa sola**: legge il cancello del
        /// co-op (`CoopGate.IsMultiplayerActive()`, il SOCKET verso il server: host o peer) e, **quando
        /// cambia**, riapplica o spegne. Non e' ridondanza con `ApplyOnSpawn`:
        ///
        ///   · il giocatore puo' **disconnettersi senza respawnare** — e li' la richiesta e' esplicita:
        ///     in single player "il debuff deve essere disattivato e **puo' curarsi**" ⇒ la vita massima
        ///     torna piena SUBITO, non alla prossima morte o al prossimo falo';
        ///   · e puo' **connettersi a meta' partita** — e li' la Colpa vera (contatore compreso) deve
        ///     tornare senza aspettare uno spawn.
        ///
        /// Costo per frame: una lettura booleana (due confronti, `NetworkManager.cs:22`) e un confronto.
        /// Nessuna scrittura se il fronte non c'e' — che e' il caso normale, quasi tutti i frame.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                bool multiplayer = CoopGate.IsMultiplayerActive();

                if (_gateKnown && multiplayer == _gateMultiplayer)
                    return;

                bool first = !_gateKnown;
                _gateKnown = true;
                _gateMultiplayer = multiplayer;

                string reason = SuppressionReason();

                if (reason != null)
                {
                    // ⭐ SPEGNERE E' UNA SCRITTURA SOLA, e scrive **1.0**: la vita torna alla larghezza del
                    //   gioco, si cura con i mezzi del gioco, e nessuna notifica dice il falso.
                    var stats = PlayerStats();
                    if (stats != null)
                        stats.Life.MaxFactor = 1f;

                    ModLog.Info(MARK + " non applicata: " + reason +
                                " · debuff SPENTO, Life.MaxFactor=1.0 · il giocatore puo' curarsi" +
                                (stats == null ? " · (Penitent assente: nessuna scrittura)" : string.Empty) +
                                (first ? " · primo giro" : " · fronte: connessione persa"));
                    return;
                }

                ApplyNow(first ? "primo giro con server" : "fronte: connessione al server");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " fronte co-op NO (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· la Colpa resta nello stato in cui era, niente di rotto");
            }
        }

        /// <summary>
        /// Una partita nuova riparte senza colpa. Non e' un dettaglio: il file e' UNO (non uno per slot),
        /// quindi senza questa riga una nuova partita ereditera' le morti della precedente. Chiamata da
        /// `Multiplayer.OnNewGame`, che il ModdingAPI invoca quando il menu principale avvia una partita
        /// NUOVA o una conversione NG+ (`ModPatches.cs:20-21`).
        /// </summary>
        internal static void OnNewGame()
        {
            try
            {
                EnsureLoaded();

                int before = _deaths;
                _deaths = 0;
                _announcedStep = 0;
                Save("nuova partita");

                ModLog.Info(MARK + " nuova partita · contatore 0 · prima=" + before.ToString(CultureInfo.InvariantCulture) + " · fattore=1.0");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " nuova partita: contatore NON azzerato (" + e.GetType().Name + ": " + e.Message + ")");
            }
        }

        // ============================================================================================
        // ④ IL TESTO — la rappresentazione scelta: TESTO, una notifica, una volta per scalino.
        // ============================================================================================

        /// <summary>
        /// Il testo proposto e' **"YOUR MAX LIFE IS REDUCED TO 80% - REST AT A PRIE-DIEU"**, in inglese
        /// e ASCII puro come le altre notifiche della mod (`PauseNotice.TEXT` e' "YOUR TEAMMATE IS
        /// PAUSED", costante e non chiave di localizzazione: stesso precedente, nessuna riga da tradurre
        /// in `resources\localization\Multiplayer.txt`).
        ///
        /// La percentuale e' quella vera del fattore (80 / 60 / 40), non un numero scritto a mano: se un
        /// giorno gli scalini cambiano, il testo non mente.
        /// </summary>
        private static void Notify(float factor)
        {
            try
            {
                // ⭐ ULTIMA GUARDIA (le altre stanno a monte): in single player / PE02 / Demake la vita NON
                //   e' stata ridotta, e un testo "YOUR MAX LIFE IS REDUCED..." direbbe il falso. Difesa in
                //   profondita', non il punto unico: costa un booleano e non puo' sbagliare da sola.
                string reason = SuppressionReason();
                if (reason != null)
                {
                    ModLog.Info(MARK + " non applicata: " + reason + " · notifica NON mostrata " +
                                "(la vita non e' stata ridotta: il testo direbbe il falso)");
                    return;
                }

                if (Main.Multiplayer == null || Main.Multiplayer.NotificationManager == null)
                {
                    ModLog.Info(MARK + " notifica NON mostrata (NotificationManager assente): la penalita' " +
                                "resta applicata, fattore=" + Text(factor));
                    return;
                }

                int percent = (int)Math.Round(factor * 100f);
                string text = "YOUR MAX LIFE IS REDUCED TO " + percent.ToString(CultureInfo.InvariantCulture) +
                              "% - REST AT A PRIE-DIEU";

                Main.Multiplayer.NotificationManager.DisplayNotification(text);

                ModLog.Info(MARK + " notifica \"" + text + "\" · UNA VOLTA per scalino " +
                            "(non a ogni spawn, non a ogni morte)");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " notifica non mostrata (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· la penalita' resta applicata lo stesso");
            }
        }

        // ============================================================================================
        // IL SALVATAGGIO — testo puro, e la robustezza prima di tutto.
        // ============================================================================================

        /// <summary>
        /// La prima lettura, una volta sola. **File assente, illeggibile, vuoto o corrotto => contatore 0,
        /// nessuna eccezione**: e' la regola del mandato, e serve perche' un file di stato non deve mai
        /// impedire di giocare. Un numero negativo vale 0; uno enorme vale `MAX_DEATHS`.
        /// </summary>
        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;

            int value = 0;
            string note = "file assente: contatore 0";

            try
            {
                string path = PathOfFile();

                if (File.Exists(path))
                {
                    note = "file presente ma senza un numero leggibile: contatore 0";

                    foreach (string raw in File.ReadAllLines(path))
                    {
                        if (raw == null)
                            continue;

                        string line = raw.Trim();
                        if (line.Length == 0 || line[0] == '#')
                            continue;

                        if (line.StartsWith(FILE_KEY, StringComparison.Ordinal))
                            line = line.Substring(FILE_KEY.Length).Trim();

                        int parsed;
                        if (!int.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                            continue;

                        if (parsed < 0)
                            parsed = 0;
                        if (parsed > MAX_DEATHS)
                            parsed = MAX_DEATHS;

                        value = parsed;
                        note = "letto";
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                value = 0;
                note = "lettura FALLITA (" + e.GetType().Name + "): contatore 0";
            }

            _deaths = value;
            _announcedStep = StepOf(_deaths);

            ModLog.Info(MARK + " caricato · morti=" + _deaths.ToString(CultureInfo.InvariantCulture) +
                        " fattore=" + Text(FactorOfCount(_deaths)) + " · " + note +
                        " · " + SafePath());
        }

        /// <summary>
        /// Scrive il contatore su disco. Il file e' piccolo e si scrive solo quando il numero CAMBIA
        /// (una morte, un falo', una partita nuova), quindi non c'e' nessun costo per frame.
        ///
        /// Il formato e' testo puro, con tre righe di commento e una riga sola di dato: chi apre il file
        /// capisce cosa c'e' scritto, e `File.ReadAllLines` non ha bisogno di un serializzatore che possa
        /// lanciare su un file mangiato a meta'.
        /// </summary>
        private static void Save(string why)
        {
            try
            {
                string path = PathOfFile();
                string dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                string text =
                    "# Blasphemous COOP - la colpa sulla vita (LA COLPA SULLA VITA)" + Environment.NewLine +
                    "# Le morti fatte da un Prie-Dieu all'altro. Si azzera SOLO sedendosi a un Prie-Dieu." + Environment.NewLine +
                    "# Un numero assente o illeggibile vale 0: cancellare questo file perdona la colpa." + Environment.NewLine +
                    FILE_KEY + _deaths.ToString(CultureInfo.InvariantCulture) + Environment.NewLine;

                File.WriteAllText(path, text);

                ModLog.Info(MARK + " salvato · contatore=" + _deaths.ToString(CultureInfo.InvariantCulture) +
                            " fattore=" + Text(FactorOfCount(_deaths)) +
                            " · " + path + " · (" + why + ")");
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " salvato NO (" + e.GetType().Name + ": " + e.Message + ") · il contatore " +
                             "(" + _deaths.ToString(CultureInfo.InvariantCulture) + ") resta in memoria: " +
                             "la penalita' funziona, ma non sopravvive alla chiusura");
            }
        }

        /// <summary>Il percorso del file: `Modding/data/Multiplayer/life-guilt.txt`, la stessa cartella dei
        /// file che la mod legge gia' (`attacks.json` e compagni).</summary>
        private static string PathOfFile()
        {
            if (_path != null)
                return _path;

            string root = null;

            try
            {
                if (Main.Multiplayer != null && Main.Multiplayer.FileHandler != null)
                    root = Main.Multiplayer.FileHandler.ModdingFolder;
            }
            catch (Exception)
            {
                // La radice si ricostruisce sotto; nessuna eccezione esce da qui.
            }

            if (string.IsNullOrEmpty(root))
                root = Path.GetFullPath("Modding" + Path.DirectorySeparatorChar);

            _path = Path.Combine(Path.Combine(Path.Combine(root, "data"), "Multiplayer"), FILE_NAME);
            return _path;
        }

        /// <summary>Il percorso, per i log, senza poter lanciare.</summary>
        private static string SafePath()
        {
            try
            {
                return PathOfFile();
            }
            catch (Exception)
            {
                return "(percorso non disponibile)";
            }
        }

        // ============================================================================================
        // I PEZZI PICCOLI.
        // ============================================================================================

        /// <summary>
        /// Riscrive il fattore sul Penitente **in scena**, quando il contatore cambia. Separata da
        /// `ApplyOnSpawn` solo per il testo del log: la scrittura e' la stessa, il clamp del setter e'
        /// quello del gioco (`VariableAttribute.cs:37`, `:86-89`).
        /// </summary>
        private static void ApplyNow(string why)
        {
            try
            {
                var stats = PlayerStats();
                if (stats == null)
                    return;

                stats.Life.MaxFactor = Factor();

                ModLog.Info(MARK + " fattore riapplicato subito (" + why + ") · vita max=" +
                            F1(stats.Life.CurrentMax) + " final=" + F0(stats.Life.Final) +
                            " current=" + F0(stats.Life.Current));
            }
            catch (Exception e)
            {
                ModLog.Error(MARK + " riapplicazione (" + why + ") NO (" + e.GetType().Name + ": " + e.Message + ") " +
                             "· verra' rifatta al prossimo spawn");
            }
        }

        /// <summary>Le statistiche del Penitente, o null. Non lancia mai.</summary>
        private static Gameplay.GameControllers.Entities.EntityStats PlayerStats()
        {
            try
            {
                if (Core.Logic == null || Core.Logic.Penitent == null)
                    return null;

                return Core.Logic.Penitent.Stats;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Lo scalino di un conteggio: 0..3. E' il numero che decide la notifica e il tetto.</summary>
        private static int StepOf(int deaths)
        {
            if (deaths <= 0)
                return 0;
            if (deaths >= LAST_STEP)
                return LAST_STEP;
            return deaths;
        }

        /// <summary>Il fattore di un conteggio, senza toccare lo stato (serve ai log del caricamento).</summary>
        private static float FactorOfCount(int deaths)
        {
            if (deaths <= 0)
                return FACTOR_NO_GUILT;
            if (deaths == 1)
                return FACTOR_ONE_DEATH;
            if (deaths == 2)
                return FACTOR_TWO_DEATHS;
            return FACTOR_LAST_STEP;
        }

        /// <summary>Il fattore in una forma leggibile e STABILE (`0.8`, non `0,8`): i log si cercano.</summary>
        private static string Text(float factor)
        {
            return factor.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>Un decimale, con il PUNTO e non la virgola: il log e' una misura, non un testo locale.</summary>
        private static string F1(float value)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>Un intero, in forma stabile (`88`, non `88,0`).</summary>
        private static string F0(float value)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// ③ IL FALO' — la patch nuova, e il conto dei bersagli lo dice: **269 -> 270**.
    ///
    /// Un **PREFIX**, non un postfix: il metodo del gioco azzera e riempie nell'ordine
    /// `Life.SetToCurrentMax()` (`PrieDieu.cs:225`) — se il fattore tornasse a 1 solo DOPO, il giocatore
    /// si alzerebbe col massimo ancora ridotto e la colpa perdonata: "il falo' non mi ha curato".
    ///
    /// E su **questo** metodo, non su `PrieDieu.OnUse`: `OnUse` e' un iteratore il cui corpo gira DOPO i
    /// postfix (gia' misurato, `docs\TODO.md:3432-3436`), e soprattutto `OnUse` **non** e' il punto in cui
    /// il gioco applica i benefici. `ShallowActivationLogic` e' l'unico punto che la replica non chiama
    /// MAI — la replica del falo' passa da `Core.Logic.UsePrieDieu()`
    /// (`EnemySyncManager.cs:3414`) — quindi il perdono e' di chi si siede, e di nessun altro.
    ///
    /// ⚠️ Il metodo e' PRIVATO: Harmony lo patcha lo stesso (e' un nome in una STRINGA, quindi
    /// `tools\verify-patch-targets.ps1` lo controlla prima del deploy: se non esistesse, `PatchAll`
    /// abortirebbe e TUTTE le patch successive non verrebbero applicate).
    /// </summary>
    [HarmonyPatch(typeof(PrieDieu), "ShallowActivationLogic")]
    public static class PrieDieuShallowActivationLogic_LifeGuilt_Patch
    {
        /// <summary>
        /// Il prefix: perdona la colpa sulla vita, e lascia che sia il gioco a riempire. Non legge ne'
        /// scrive niente del `PrieDieu`: il metodo originale gira subito dopo, identico.
        /// </summary>
        [HarmonyPrefix]
        public static void Prefix()
        {
            LifeGuilt.ResetAtBonfire();
        }
    }
}
