using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Entities.Weapon;
using Gameplay.GameControllers.Enemies.BellGhost;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Enemies.Projectiles;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⚠️ SONDA — DA RIMUOVERE DOPO LA MISURA. NON E' UN FIX.
    ///
    /// SOLO LOG: legge, non scrive. Nessun `Prefix` che ritorna false, nessun `Kill/Damage/Death`,
    /// nessuna `Init()`, nessun `SetOwner`, nessuna modifica ai campi che legge. Gli unici effet-
    /// ti collaterali sono allocazioni e righe di log, e ogni punto d'ingresso e' chiuso in try/catch
    /// perche' una sonda non deve mai rompere il gioco (vedi `Fail`).
    ///
    /// PERCHE' ESISTE (studio: `scratch\STUDIO-CANALE-EFFETTI.md`, §3.4 e §7). Lo studio ha stabilito
    /// che il gioco ha UN canale di istanziazione, `PoolManager.ReuseObject(prefab, position,
    /// rotation)`, ma non ha potuto verificare cinque cose senza il gioco vivo:
    ///
    ///   1. ⭐ IL VOCABOLARIO REALE: quali prefab finiscono nel pool, e quali vengono davvero riusati
    ///      durante una fight. `ReuseObject` riceve il prefab: qui si stampa `prefab.name`, posizione
    ///      e rotazione. I pool esistenti si elencano SENZA patcharli: `CreatePool` li crea come
    ///      figli del `PoolManager` con nome `prefab.name + " pool"` (`PoolManager.cs:78-80`), quindi
    ///      basta leggere i figli del singleton.
    ///   2. ⭐ IL TIPO DELL'AREA: la strusciata di ElderBrother e' un `BossSpawnedAreaAttack` o una
    ///      `ShockwaveArea`? Il tipo decide se `SetOwner` viene chiamato, cioe' se l'attribuzione e'
    ///      possibile: `BossAreaSummonAttack.InstantiateArea` fa `GetComponent&lt;BossSpawnedAreaAttack&gt;()`
    ///      e chiama `SetOwner` SOLO se il cast riesce. Qui si stampa il tipo reale del componente
    ///      `Weapon` sull'istanza riusata.
    ///   3. ⭐ `WeaponOwner`: subito dopo `ReuseObject` e' `null`? E dopo un frame? Si stampano
    ///      `ownerSubito` (letto NEL Postfix, prima che il chiamante chiami `SetOwner`) e `ownerDopo`
    ///      (letto nel LateUpdate dello stesso frame, o al piu' tardi un frame dopo).
    ///   4. ⚠️ LA RISOLUZIONE `nome -> prefab` SUL RICEVENTE: dai campi `GameObject`/`List<GameObject>`
    ///      della copia locale del boss si arriva al prefab? Si elencano, per riflessione, i campi dei
    ///      componenti del boss e di quelli d'attacco che referenziano (`areaAttack`, `jumpAttack`...),
    ///      stampando `nome del campo -> nome del GameObject`. Poi, per ogni nome visto, si dice se e'
    ///      stato trovato in quei campi, se esiste un pool locale e se `Resources.FindObjectsOfTypeAll`
    ///      lo restituisce (le tre strade di §3.4).
    ///   5. ⚠️ L'ORDINE: `ReuseObject` prima o dopo `SetOwner`? Lo dicono (3) e il `frame+` di (4).
    ///
    /// E IN PIU' (aggiunta dell'utente): i PROIETTILI sono prefab in pool come gli effetti, quindi
    /// passano dallo stesso hook. Per ognuno si registra: tipo, `Weapon`/`Projectile`, `Rigidbody2D`,
    /// i componenti per nome, il bersaglio (`HomingProjectile.TargetsPenitent`/`AlternativeTarget`),
    /// la velocita' (`Projectile.velocity`, che il chiamante scrive DOPO `ReuseObject`), il TTL
    /// (`Projectile.timeToLive`, cioe' se e' effimero) e `ProjectileWeapon.AttackingEntity`.
    ///
    /// IL FILTRO (dichiarato, e' la scelta che conta). Non si puo' filtrare per scena (altri boss
    /// vivono altrove, e "durante una bossfight" non e' un predicato affidabile), ne' per posizione.
    /// Il filtro e' STRUTTURALE, e' lo stesso che userebbe il relay:
    ///
    ///     prefab.GetComponentInChildren&lt;Weapon&gt;(true) != null
    ///       || prefab.GetComponentInChildren&lt;Projectile&gt;(true) != null
    ///
    /// cioe': o il prefab FERISCE (ogni cosa che ferisce e' una `Weapon : PoolObject`, 69 sottoclassi),
    /// o il prefab VOLA (un `Projectile`). Fuori restano i cosmetici puri (polvere, scintille), che
    /// sarebbero la parte rumorosa e che il relay non copre comunque. Il filtro si calcola UNA volta
    /// per prefab (cache per `GetInstanceID`) e il risultato e' 0/1: e' la stessa riga che decide il
    /// pool in `BossJumpAttack:228`, quindi non e' un'euristica.
    ///
    /// I TETTI (perche' il canale e' di TUTTI, non dei boss: ci passano giocatore, trappole, ambiente):
    ///   · massimo 10 righe di dettaglio per NOME di prefab, per scena (una sequenza di summon intera
    ///     si vede; un ciclo infinito no). Oltre: solo conteggio, che finisce nel riepilogo;
    ///   · massimo 400 righe di dettaglio per SCENA, poi la sonda tace fino alla scena dopo (il tetto
    ///     e' per scena e non per sessione di proposito: una scena esplorata PRIMA della bossfight non
    ///     deve poter consumare il budget della bossfight, che e' la misura che conta);
    ///   · massimo 1500 righe di dettaglio per SESSIONE: la rete di sicurezza assoluta, una volta sola;
    ///   · censimento dei pool: massimo 200 righe per scena;
    ///   · campi del boss: massimo 80 righe per boss;
    ///   · coda delle letture differite: massimo 300 voci (le eccedenti si contano, non si perdono).
    /// Le prime due righe di ogni nome sono un "cartellino" (tipo, componenti, segnali), poi una riga
    /// per chiamata con posizione e rotazione.
    ///
    /// COSTO: 268 -> 269 bersagli Harmony (verificato con `tools\verify-patch-targets.ps1`).
    /// ZERO protocollo, ZERO server, ZERO righe di rete: questa sonda non manda e non riceve nulla.
    ///
    /// COME SI TOGLIE: cancellare questo file e le due righe marcate `TEMPORARY (EffectPoolProbe)` in
    /// `Multiplayer.cs` (`OnLevelLoaded`, `OnLateUpdate`). Nessun'altra riga del progetto la cita.
    /// </summary>
    [HarmonyPatch(typeof(PoolManager), nameof(PoolManager.ReuseObject))]
    internal static class EffectPoolProbe
    {
        // ============================================================================================
        //  I MARKER. Ognuno separa una domanda diversa nel log; non si sovrappongono a marker esistenti.
        // ============================================================================================
        private const string MARK_POOL = "[FxPool]";              // una chiamata filtrata (posizione, rotazione, owner)
        private const string MARK_NULLO = "[FxPool NULLO]";       // ReuseObject ha restituito null: il pool non c'era
        private const string MARK_CENSUS = "[FxCensimento]";      // un pool esistente nel PoolManager
        private const string MARK_BOSS = "[FxBoss]";              // il boss e i suoi campi GameObject
        private const string MARK_RESOLVE = "[FxRisolvi]";        // nome -> riferimento (boss / pool / Resources)
        private const string MARK_SUMMARY = "[FxRiepilogo]";      // fine scena: conteggi e nomi
        private const string MARK_FAIL = "[FxSonda ERR]";         // guasto della sonda, non del gioco

        // ============================================================================================
        //  I TETTI. Vedi il commento di classe: sono la ragione per cui questa sonda non inonda il log.
        // ============================================================================================
        private const int MAX_PRINTED_PER_NAME = 10;        // righe di dettaglio per nome, per scena
        private const int MAX_DETAIL_LINES = 400;           // per SCENA: raggiunto, la sonda tace fino alla scena dopo
        private const int MAX_DETAIL_LINES_SESSION = 1500;  // SESSIONE: rete di sicurezza assoluta
        private const int MAX_CENSUS_LINES = 200;           // righe di censimento per scena
        private const int MAX_BOSS_FIELD_LINES = 80;        // righe per boss
        private const int MAX_PENDING = 300;                // letture differite in coda
        private const int MAX_SIGNALS = 14;                 // componenti elencati per prefab

        private const float CENSUS_SECONDS = 5f;       // i pool nascono negli OnStart: si riguarda
        private const float BOSS_SCAN_SECONDS = 5f;    // i boss si accendono dopo la cutscene
        private const float RESOLVE_SECONDS = 2f;      // risoluzione nome -> riferimento
        private const float RESOLVE_INDEX_SECONDS = 10f; // ricostruzione di Resources.FindObjectsOfTypeAll

        // ============================================================================================
        //  STATO. Tutto per scena, tranne il tetto di sessione: `_detailLines` NON si azzera.
        // ============================================================================================

        private static bool _inLevel;
        private static string _scene = string.Empty;

        private static int _calls;                                                     // chiamate filtrate
        private static readonly Dictionary<string, int> _callsByName = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _printedByName = new Dictionary<string, int>();
        private static readonly Dictionary<string, string> _classeByName = new Dictionary<string, string>();
        private static readonly Dictionary<string, string> _signalsByName = new Dictionary<string, string>();
        private static readonly HashSet<string> _knownNames = new HashSet<string>();
        private static readonly HashSet<string> _cardPrinted = new HashSet<string>();

        /// <summary>Cache del filtro, per prefab: la risposta non cambia durante la sessione.</summary>
        private static readonly Dictionary<int, bool> _interesting = new Dictionary<int, bool>();

        private static int _sceneDetailLines;   // PER SCENA: si azzera a ogni LevelLoaded
        private static bool _sceneFuseReported; // PER SCENA
        private static int _detailLines;        // SESSIONE: non si azzera per scena
        private static bool _fuseReported;      // SESSIONE

        private static readonly List<Call> _pending = new List<Call>();
        private static int _dropped;

        private static float _censusTimer;
        private static readonly HashSet<string> _censusHolders = new HashSet<string>();
        private static int _censusLines;
        private static bool _censusTruncated;
        private static bool _poolManagerMissing;

        private static float _bossTimer;
        private static readonly HashSet<int> _bossDumped = new HashSet<int>();
        private static readonly Dictionary<string, string> _bossFieldByName = new Dictionary<string, string>();

        private static float _resolveTimer;
        private static readonly List<string> _resolvePending = new List<string>();
        private static readonly HashSet<string> _resolveQueued = new HashSet<string>();
        private static Dictionary<string, GameObject> _resourcesIndex;
        private static float _resourcesIndexAt = -1000f;

        private static readonly Dictionary<Type, bool> _typeHasGo = new Dictionary<Type, bool>();

        // ============================================================================================
        //  L'HOOK. Un `Postfix` su `PoolManager.ReuseObject`: LEGGE soltanto.
        //
        //  Perche' un Postfix e non un Prefix: un Prefix potrebbe saltare il corpo originale, un
        //  Postfix no. Il corpo del gioco gira identico, qualunque cosa faccia questa sonda.
        //
        //  Qui si fa il minimo: filtro, conteggi e accodamento. La stampa avviene in `Tick` (LateUpdate
        //  dello stesso frame) perche' cosi' si legge lo stato DOPO che il chiamante ha fatto il suo
        //  lavoro (`SetOwner`, `SetDamage`, `Init` della velocita') senza perdere il valore letto QUI,
        //  che e' quello "subito dopo ReuseObject" e risponde alla domanda 3 e 5.
        // ============================================================================================
        [HarmonyPostfix]
        private static void Postfix(GameObject prefab, Vector3 position, Quaternion rotation,
                                    PoolManager.ObjectInstance __result)
        {
            try
            {
                if (prefab == null)
                    return;

                if (!IsInteresting(prefab))
                    return;

                string name = Clean(prefab.name);
                if (string.IsNullOrEmpty(name))
                    return;

                _calls++;

                int count;
                _callsByName.TryGetValue(name, out count);
                _callsByName[name] = count + 1;
                _knownNames.Add(name);
                QueueResolve(name);

                if (_pending.Count >= MAX_PENDING)
                {
                    _dropped++;
                    return;
                }

                bool nullResult = __result == null;
                GameObject instance = nullResult ? null : __result.GameObject;

                Call call = new Call();
                call.Name = name;
                call.Instance = instance;
                call.Pos = position;
                call.RotZ = rotation.eulerAngles.z;
                call.Frame = Time.frameCount;
                call.Seq = _calls;
                call.ResultWasNull = nullResult;
                // ⭐ La lettura che risponde alla domanda 3: QUI il chiamante non ha ancora chiamato
                // `SetOwner` (avviene subito dopo, nello stesso corpo), quindi `null` e' la risposta
                // attesa; se invece e' gia' valorizzato, e' un valore STANTIO del riuso precedente.
                call.OwnerAtCall = nullResult ? "nessuna_istanza" : OwnerNow(instance);

                _pending.Add(call);
            }
            catch (Exception e)
            {
                Fail("Postfix", e);
            }
        }

        // ============================================================================================
        //  IL CICLO. Riusa `Multiplayer.OnLateUpdate` (chiamato da `Multiplayer.cs`): una sonda non
        //  merita un MonoBehaviour suo. Zero bersagli Harmony aggiuntivi.
        // ============================================================================================
        internal static void Tick()
        {
            try
            {
                if (!_inLevel)
                    return;

                FlushPending();

                _censusTimer += Time.unscaledDeltaTime;
                if (_censusTimer >= CENSUS_SECONDS)
                {
                    _censusTimer = 0f;
                    Census();
                }

                _bossTimer += Time.unscaledDeltaTime;
                if (_bossTimer >= BOSS_SCAN_SECONDS)
                {
                    _bossTimer = 0f;
                    ScanBosses();
                }

                _resolveTimer += Time.unscaledDeltaTime;
                if (_resolveTimer >= RESOLVE_SECONDS)
                {
                    _resolveTimer = 0f;
                    ResolvePass();
                }
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        /// <summary>
        /// Chiamata da `Multiplayer.OnLevelLoaded`: chiude il riepilogo della scena che si lascia,
        /// azzera lo stato per-scena e riarma la sonda. Il nome della scena arriva da qui e non da
        /// letture indirette: e' l'evento che il gioco emette quando il livello e' pronto.
        /// </summary>
        internal static void LevelLoaded(string level)
        {
            try
            {
                if (!string.IsNullOrEmpty(_scene) && _scene != level)
                    PrintSummary();

                ResetScene(level);

                if (_inLevel)
                {
                    ModLog.Info(MARK_POOL + " SONDA ARMATA scena=" + level +
                                " filtro=Weapon|Projectile max/nome=" + MAX_PRINTED_PER_NAME +
                                " tetto=" + MAX_DETAIL_LINES + " (SOLO LOG: non cambia niente)");
                }
            }
            catch (Exception e)
            {
                Fail("LevelLoaded", e);
            }
        }

        private static void ResetScene(string level)
        {
            _scene = level ?? string.Empty;
            _inLevel = !string.IsNullOrEmpty(_scene) && _scene != "MainMenu";

            _calls = 0;
            _callsByName.Clear();
            _printedByName.Clear();
            _classeByName.Clear();
            _signalsByName.Clear();
            _knownNames.Clear();
            _cardPrinted.Clear();

            _pending.Clear();
            _dropped = 0;

            _censusTimer = 0f;
            _censusHolders.Clear();
            _censusLines = 0;
            _censusTruncated = false;
            _poolManagerMissing = false;

            _bossTimer = 0f;
            _bossDumped.Clear();
            _bossFieldByName.Clear();

            _resolveTimer = 0f;
            _resolvePending.Clear();
            _resolveQueued.Clear();
            _resourcesIndex = null;      // gli oggetti di scena sono cambiati
            _resourcesIndexAt = -1000f;

            // PER SCENA: si azzera. E' il tetto che protegge la bossfight da una scena esplorata prima.
            _sceneDetailLines = 0;
            _sceneFuseReported = false;

            // `_interesting` NON si azzera: i prefab sono asset e la risposta non cambia.
            // `_detailLines` e `_fuseReported` NON si azzerano: il tetto di SESSIONE e' assoluto.
        }

        // ============================================================================================
        //  LA STAMPA DELLE CHIAMATE, differita di un frame per leggere lo stato finale.
        // ============================================================================================

        private static void FlushPending()
        {
            if (_pending.Count == 0)
                return;

            int keep = 0;
            for (int i = 0; i < _pending.Count; i++)
            {
                Call call = _pending[i];
                if (TryPrint(call))
                    continue;

                _pending[keep++] = call;
            }

            if (keep < _pending.Count)
                _pending.RemoveRange(keep, _pending.Count - keep);
        }

        /// <summary>
        /// Stampa una chiamata se e' il momento. Ritorna false per lasciarla in coda: succede solo se
        /// `WeaponOwner` e' ancora nullo e non e' ancora passato un frame (il chiamante puo' assegnarlo
        /// da una coroutine, e un frame lo copre).
        /// </summary>
        private static bool TryPrint(Call call)
        {
            if (call.Instance == null)
            {
                // Due casi diversi, e vanno tenuti separati: il pool non c'era al momento della
                // chiamata (il gioco ha ricevuto `null`), oppure l'istanza e' stata distrutta prima
                // della lettura differita (effimera: capita ai proiettili).
                if (call.ResultWasNull)
                    PrintNullResult(call);
                else
                    PrintInstanceGone(call);

                return true;
            }

            string ownerNow = OwnerNow(call.Instance);
            bool ownerKnown = ownerNow != "null";
            bool lastChance = Time.frameCount > call.Frame + 1;

            if (!ownerKnown && !lastChance)
                return false;

            PrintCall(call, ownerNow);
            return true;
        }

        private static void PrintNullResult(Call call)
        {
            int printed;
            _printedByName.TryGetValue(call.Name, out printed);
            if (!TakeDetailBudget(call.Name, printed))
                return;

            _printedByName[call.Name] = printed + 1;
            CountDetailLine();

            ModLog.Info(MARK_NULLO + " scena=" + _scene + " n=" + call.Seq + " prefab=\"" + call.Name +
                        "\" pos=(" + F(call.Pos.x) + "," + F(call.Pos.y) + ") rotZ=" + F(call.RotZ) +
                        " motivo=ReuseObject ha restituito null (pool non creato; il gioco non ha mostrato l'effetto)");
        }

        private static void PrintInstanceGone(Call call)
        {
            int printed;
            _printedByName.TryGetValue(call.Name, out printed);
            if (!TakeDetailBudget(call.Name, printed))
                return;

            _printedByName[call.Name] = printed + 1;
            CountDetailLine();

            ModLog.Info(MARK_POOL + " scena=" + _scene + " n=" + call.Seq + " prefab=\"" + call.Name +
                        "\" pos=(" + F(call.Pos.x) + "," + F(call.Pos.y) + ") rotZ=" + F(call.RotZ) +
                        " istanza=<distrutta prima della lettura differita> ownerSubito=" + call.OwnerAtCall);
        }

        /// <summary>
        /// I tetti: per nome, per scena e per sessione. Ritorna false quando la riga non va stampata.
        /// Il conteggio delle chiamate resta comunque, e finisce nel riepilogo: la misura non si perde.
        /// </summary>
        private static bool TakeDetailBudget(string name, int printed)
        {
            if (printed >= MAX_PRINTED_PER_NAME)
                return false;

            // ⭐ PER SCENA, e non per sessione: una scena esplorata PRIMA della bossfight non deve
            // consumare il budget della bossfight. Si tace fino al prossimo caricamento di livello.
            if (_sceneDetailLines >= MAX_DETAIL_LINES)
            {
                if (!_sceneFuseReported)
                {
                    _sceneFuseReported = true;
                    ModLog.Info(MARK_POOL + " SONDA IN SILENZIO PER QUESTA SCENA: raggiunte " +
                                MAX_DETAIL_LINES + " righe di dettaglio. Da qui solo conteggi, " +
                                "censimento, campi del boss e riepilogo; il dettaglio riprende alla " +
                                "prossima scena.");
                }

                return false;
            }

            // La rete di sicurezza assoluta, per una sessione lunghissima.
            if (_detailLines >= MAX_DETAIL_LINES_SESSION)
            {
                if (!_fuseReported)
                {
                    _fuseReported = true;
                    ModLog.Info(MARK_POOL + " SONDA SPENTA: raggiunte " + MAX_DETAIL_LINES_SESSION +
                                " righe di dettaglio nella SESSIONE. Da qui solo conteggi, censimento, " +
                                "campi del boss e riepilogo.");
                }

                return false;
            }

            return true;
        }

        /// <summary>Una riga di dettaglio e' stata scritta: si contano entrambi i tetti.</summary>
        private static void CountDetailLine()
        {
            _detailLines++;
            _sceneDetailLines++;
        }

        private static void PrintCall(Call call, string ownerNow)
        {
            int printed;
            _printedByName.TryGetValue(call.Name, out printed);

            if (!TakeDetailBudget(call.Name, printed))
                return;

            bool card = !_cardPrinted.Contains(call.Name);
            if (card)
                PrintCard(call);

            _cardPrinted.Add(call.Name);
            _printedByName[call.Name] = printed + 1;
            CountDetailLine();

            Projectile projectile = FirstProjectile(call.Instance);
            string projectileOwner = projectile != null ? ClassifyEntity(projectile.owner) : "-";
            string velocity = "-";
            string aim = "-";

            if (projectile != null)
            {
                velocity = "(" + F(projectile.velocity.x) + "," + F(projectile.velocity.y) + ")";
                aim = AimOf(call.Instance);
            }

            ModLog.Info(MARK_POOL + " scena=" + _scene + " n=" + call.Seq + " prefab=\"" + call.Name +
                        "\" pos=(" + F(call.Pos.x) + "," + F(call.Pos.y) + ") rotZ=" + F(call.RotZ) +
                        " ownerSubito=" + call.OwnerAtCall +
                        " ownerDopo=" + ownerNow +
                        " projOwner=" + projectileOwner +
                        " vel=" + velocity +
                        " punta=" + aim);
        }

        /// <summary>
        /// Il cartellino del prefab, una volta per nome: e' la riga che dice COSA e' l'effetto, e
        /// risponde da sola alla domanda 2 (il tipo dell'area: `BossSpawnedAreaAttack` o
        /// `ShockwaveArea`?) e alla distinzione stazionario/proiettile.
        /// </summary>
        private static void PrintCard(Call call)
        {
            Weapon weapon;
            Projectile projectile;
            string classe = ClasseOf(call.Instance, out weapon, out projectile);
            string weaponType = weapon != null ? weapon.GetType().Name : "-";
            string projectileType = projectile != null ? projectile.GetType().Name : "-";
            string ttl = projectile != null ? F(projectile.timeToLive) : "-";
            string signals = Segnali(call.Instance, MAX_SIGNALS);
            string attacking = AttackingEntityOf(call.Instance);
            string aimDefault = projectile != null ? AimOf(call.Instance) : "-";

            _classeByName[call.Name] = classe;
            _signalsByName[call.Name] = signals;

            CountDetailLine();

            ModLog.Info(MARK_POOL + " nuovo scena=" + _scene + " prefab=\"" + call.Name + "\"" +
                        " classe=" + classe +
                        " arma=" + weaponType +
                        " proj=" + projectileType +
                        " rb=" + (HasRigidbody(call.Instance) ? "si" : "no") +
                        " ttl=" + ttl +
                        " bersaglio=" + aimDefault +
                        " attaccante=" + attacking +
                        " segnali=" + signals +
                        " istanza=\"" + Clean(call.Instance.name) + "\"");
        }

        // ============================================================================================
        //  IL CENSIMENTO DEI POOL (domanda 1, prima parte). Zero patch: `CreatePool` crea i pool come
        //  figli del `PoolManager`, con nome `prefab.name + " pool"` (PoolManager.cs:74-100). Quindi i
        //  figli del singleton SONO il vocabolario, compresi i prefab che il filtro non intercetta.
        // ============================================================================================
        private static void Census()
        {
            PoolManager manager = PoolManager.Instance;
            if (manager == null)
            {
                if (!_poolManagerMissing)
                {
                    _poolManagerMissing = true;
                    ModLog.Info(MARK_CENSUS + " scena=" + _scene +
                                " PoolManager.Instance=null (in questa scena non c'e' un PoolManager: " +
                                "nessun effetto passa dal pool)");
                }

                return;
            }

            Transform root;
            try
            {
                root = manager.transform;
            }
            catch (Exception)
            {
                return;
            }

            if (root == null)
                return;

            int count = root.childCount;
            for (int i = 0; i < count; i++)
            {
                Transform holder;
                try
                {
                    holder = root.GetChild(i);
                }
                catch (Exception)
                {
                    continue;
                }

                if (holder == null)
                    continue;

                string holderName = holder.name;
                if (string.IsNullOrEmpty(holderName) || _censusHolders.Contains(holderName))
                    continue;

                if (_censusLines >= MAX_CENSUS_LINES)
                {
                    if (!_censusTruncated)
                    {
                        _censusTruncated = true;
                        ModLog.Info(MARK_CENSUS + " scena=" + _scene + " TRONCATO: raggiunte " +
                                    MAX_CENSUS_LINES + " righe di censimento in questa scena");
                    }

                    return;
                }

                _censusHolders.Add(holderName);
                _censusLines++;

                string prefabName = PrefabOfPoolHolder(holderName);
                GameObject sample = holder.childCount > 0 ? holder.GetChild(0).gameObject : null;
                int instances = holder.childCount;
                int active = 0;
                for (int c = 0; c < holder.childCount; c++)
                {
                    try
                    {
                        if (holder.GetChild(c).gameObject.activeSelf)
                            active++;
                    }
                    catch (Exception)
                    {
                    }
                }

                Weapon weapon;
                Projectile projectile;
                string classe = ClasseOf(sample, out weapon, out projectile);
                string weaponType = weapon != null ? weapon.GetType().Name : "-";
                string projectileType = projectile != null ? projectile.GetType().Name : "-";
                string signals = Segnali(sample, MAX_SIGNALS);

                _knownNames.Add(prefabName);
                QueueResolve(prefabName);
                if (sample != null && !_classeByName.ContainsKey(prefabName))
                {
                    _classeByName[prefabName] = classe;
                    _signalsByName[prefabName] = signals;
                }

                ModLog.Info(MARK_CENSUS + " scena=" + _scene + " pool=" + _censusHolders.Count +
                            " nome=\"" + holderName + "\" prefab=\"" + prefabName + "\"" +
                            " classe=" + classe +
                            " arma=" + weaponType +
                            " proj=" + projectileType +
                            " rb=" + (HasRigidbody(sample) ? "si" : "no") +
                            " istanze=" + instances +
                            " attive=" + active +
                            " segnali=" + signals);
            }
        }

        // ============================================================================================
        //  I CAMPI DEL BOSS (domanda 4). Per riflessione, in sola lettura: nome del campo -> nome del
        //  GameObject. Si seguono anche i riferimenti ai componenti d'attacco (`areaAttack`,
        //  `jumpAttack`...), perche' i campi `areaPrefab`/`objectsToInstantiate` vivono LA'.
        // ============================================================================================
        private static void ScanBosses()
        {
            List<Enemy> bosses = FindBosses();
            int before = _bossDumped.Count;

            for (int i = 0; i < bosses.Count; i++)
            {
                Enemy boss = bosses[i];
                if (boss == null)
                    continue;

                int id = boss.GetInstanceID();
                if (_bossDumped.Contains(id))
                    continue;

                _bossDumped.Add(id);

                try
                {
                    DumpBoss(boss);
                }
                catch (Exception e)
                {
                    Fail("DumpBoss", e);
                }
            }

            if (_bossDumped.Count > before)
                RequeueAllNames();
        }

        private static List<Enemy> FindBosses()
        {
            List<Enemy> found = new List<Enemy>();

            try
            {
                GameObject[] roots = SceneManager.GetActiveScene().GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    GameObject root = roots[i];
                    if (root == null)
                        continue;

                    // `true` = anche i figli spenti: un boss resta inattivo fino alla sua cutscene,
                    // ed e' proprio lo stato in cui il ricevente potrebbe dover risolvere i nomi.
                    Enemy[] inRoot = root.GetComponentsInChildren<Enemy>(true);
                    for (int j = 0; j < inRoot.Length; j++)
                    {
                        Enemy enemy = inRoot[j];
                        if (enemy == null || !IsBossType(enemy))
                            continue;

                        if (!found.Contains(enemy))
                            found.Add(enemy);
                    }
                }
            }
            catch (Exception e)
            {
                Fail("FindBosses", e);
            }

            return found;
        }

        private static bool IsBossType(Enemy enemy)
        {
            string space = enemy.GetType().Namespace;
            return !string.IsNullOrEmpty(space) &&
                   space.StartsWith("Gameplay.GameControllers.Bosses", StringComparison.Ordinal);
        }

        private static void DumpBoss(Enemy boss)
        {
            GameObject root = boss.gameObject;
            string bossType = boss.GetType().Name;

            int components;
            try
            {
                components = root.GetComponentsInChildren<Component>(true).Length;
            }
            catch (Exception)
            {
                components = -1;
            }

            bool puppet = false;
            try
            {
                puppet = EnemySyncManager.IsPuppet(boss);
            }
            catch (Exception)
            {
            }

            ModLog.Info(MARK_BOSS + " scena=" + _scene + " boss=" + bossType +
                        " nome=\"" + Clean(root.name) + "\"" +
                        " pos=(" + F(root.transform.position.x) + "," + F(root.transform.position.y) + ")" +
                        " attivo=" + root.activeInHierarchy +
                        " puppet=" + puppet +
                        " componenti=" + components);

            List<CompRef> queue = new List<CompRef>();
            HashSet<Component> visited = new HashSet<Component>();

            Component[] own;
            try
            {
                own = root.GetComponentsInChildren<Component>(true);
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < own.Length; i++)
            {
                Component component = own[i];
                if (component == null || !IsReflectable(component))
                    continue;

                if (visited.Add(component))
                    queue.Add(new CompRef(component, string.Empty, 0));
            }

            int lines = 0;
            for (int qi = 0; qi < queue.Count; qi++)
            {
                CompRef reference = queue[qi];
                if (reference.Comp == null)
                    continue;

                if (lines >= MAX_BOSS_FIELD_LINES)
                {
                    ModLog.Info(MARK_BOSS + " boss=" + bossType + " TRONCATO a " + MAX_BOSS_FIELD_LINES +
                                " righe di campi (il resto e' taciuto, non perso: il campo si cerca per nome)");
                    return;
                }

                Type type = reference.Comp.GetType();
                string label = Label(reference);

                FieldInfo[] fields;
                try
                {
                    fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                catch (Exception)
                {
                    continue;
                }

                for (int fi = 0; fi < fields.Length; fi++)
                {
                    FieldInfo field = fields[fi];

                    object value;
                    try
                    {
                        value = field.GetValue(reference.Comp);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    Type fieldType = field.FieldType;

                    if (fieldType == typeof(GameObject))
                    {
                        LogBossGo(label, field.Name, value as GameObject, ref lines);
                        continue;
                    }

                    Type element = ElementTypeOf(fieldType);
                    if (element != null)
                    {
                        IList list = value as IList;
                        if (list == null)
                            continue;

                        if (element == typeof(GameObject))
                        {
                            if (list.Count == 0)
                            {
                                ModLog.Info(MARK_BOSS + " comp=" + label + " campo=" + field.Name +
                                            " → GO <lista vuota>");
                                lines++;
                                continue;
                            }

                            for (int i = 0; i < list.Count; i++)
                            {
                                LogBossGo(label, field.Name + "[" + i + "]", list[i] as GameObject, ref lines);
                                if (lines >= MAX_BOSS_FIELD_LINES)
                                    break;
                            }
                        }
                        else if (IsPlainData(element) && TypeHasGameObject(element, 1))
                        {
                            // ⭐ E' il caso `objectsToInstantiate[].prefabToInstantiate`: una lista di
                            // struct/classi serializzabili che CONTENGONO il prefab.
                            for (int i = 0; i < list.Count; i++)
                            {
                                object item = list[i];
                                if (item == null)
                                    continue;

                                DumpPlainData(label, field.Name + "[" + i + "]", item, ref lines, 1);
                                if (lines >= MAX_BOSS_FIELD_LINES)
                                    break;
                            }
                        }

                        continue;
                    }

                    if (IsPlainData(fieldType) && fieldType != typeof(GameObject) && TypeHasGameObject(fieldType, 1))
                    {
                        if (value != null)
                            DumpPlainData(label, field.Name, value, ref lines, 1);

                        continue;
                    }

                    // I riferimenti ai componenti d'attacco: `areaAttack`, `maceImpact`, `jumpAttack`...
                    // Si seguono SOLO se sono `EnemyAttack` (il tipo che porta i campi dei prefab) e per
                    // al massimo due passi, per non tirare dentro l'intera scena dalle loro referenze.
                    Component component = value as Component;
                    if (component != null && component is EnemyAttack && reference.Depth < 2)
                    {
                        if (visited.Add(component))
                        {
                            queue.Add(new CompRef(component, field.Name, reference.Depth + 1));
                            ModLog.Info(MARK_BOSS + " comp=" + label + " campo=" + field.Name +
                                        " → componente " + component.GetType().Name);
                            lines++;
                        }
                    }
                }
            }
        }

        private static void DumpPlainData(string compLabel, string prefix, object value, ref int lines, int depth)
        {
            if (value == null || depth > 2)
                return;

            Type type = value.GetType();

            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (Exception)
            {
                return;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];

                if (field.FieldType == typeof(GameObject))
                {
                    object raw;
                    try
                    {
                        raw = field.GetValue(value);
                    }
                    catch (Exception)
                    {
                        continue;
                    }

                    LogBossGo(compLabel, prefix + "." + field.Name, raw as GameObject, ref lines);
                    continue;
                }

                if (!IsPlainData(field.FieldType) || !TypeHasGameObject(field.FieldType, 1))
                    continue;

                object inner;
                try
                {
                    inner = field.GetValue(value);
                }
                catch (Exception)
                {
                    continue;
                }

                DumpPlainData(compLabel, prefix + "." + field.Name, inner, ref lines, depth + 1);
            }
        }

        private static void LogBossGo(string compLabel, string field, GameObject go, ref int lines)
        {
            if (lines >= MAX_BOSS_FIELD_LINES)
                return;

            if (go == null)
            {
                ModLog.Info(MARK_BOSS + " comp=" + compLabel + " campo=" + field + " → GO <null>");
                lines++;
                return;
            }

            string name = Clean(go.name);

            Weapon weapon;
            Projectile projectile;
            string classe = ClasseOf(go, out weapon, out projectile);
            string weaponType = weapon != null ? weapon.GetType().Name : "-";

            ModLog.Info(MARK_BOSS + " comp=" + compLabel + " campo=" + field +
                        " → GO \"" + name + "\" classe=" + classe + " arma=" + weaponType);
            lines++;

            if (!_bossFieldByName.ContainsKey(name))
                _bossFieldByName[name] = compLabel + "." + field;
        }

        private static void RequeueAllNames()
        {
            _resolveQueued.Clear();
            foreach (string name in _knownNames)
                QueueResolve(name);
        }

        // ============================================================================================
        //  RISOLUZIONE nome -> riferimento (domanda 4, la parte dura dello studio §3.4). Le tre strade,
        //  misurate sulle STESSE righe e senza toccare niente:
        //   (1) i campi serializzati della copia locale del boss   -> boss=si(campo=...)
        //   (2) `GameObject.Find(prefabName + " pool")`            -> poolLocale=si
        //   (3) `Resources.FindObjectsOfTypeAll<GameObject>()`     -> resources=si(asset=si|no,attivo=si|no)
        // ============================================================================================
        private static void QueueResolve(string name)
        {
            if (string.IsNullOrEmpty(name) || !_resolveQueued.Add(name))
                return;

            _resolvePending.Add(name);
        }

        private static void ResolvePass()
        {
            if (_resolvePending.Count == 0)
                return;

            // Deduplica: `RequeueAllNames` (dopo il dump di un boss) puo' aver riaccodato un nome che
            // era ancora in attesa, e due righe identiche sarebbero solo rumore.
            List<string> names = new List<string>();
            HashSet<string> unique = new HashSet<string>();
            for (int i = 0; i < _resolvePending.Count; i++)
            {
                if (unique.Add(_resolvePending[i]))
                    names.Add(_resolvePending[i]);
            }

            _resolvePending.Clear();

            // ⚠️ `Resources.FindObjectsOfTypeAll` e' la chiamata piu' pesante della sonda: si tiene un
            // indice per scena e lo si ricostruisce al massimo ogni RISOLVE_INDEX_SECONDS. Nel mezzo,
            // un nome nuovo usa l'indice gia' costruito: i prefab sono caricati col livello, quindi la
            // staleness possibile e' solo quella di un pool creato dopo, non un falso "no" sui prefab.
            Dictionary<string, GameObject> index = GetResourcesIndex();

            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];

                GameObject pool = null;
                try
                {
                    pool = GameObject.Find(name + " pool");
                }
                catch (Exception)
                {
                }

                string bossField = null;
                bool inBoss = _bossFieldByName.TryGetValue(name, out bossField);

                GameObject found = null;
                if (index != null)
                    index.TryGetValue(name, out found);

                bool foundInScene = false;
                bool foundAsset = false;
                bool foundActive = false;

                try
                {
                    if (found != null)
                    {
                        foundInScene = found.scene.IsValid();
                        foundAsset = !foundInScene;
                        foundActive = foundInScene && found.activeInHierarchy;
                    }
                }
                catch (Exception)
                {
                }

                int calls;
                _callsByName.TryGetValue(name, out calls);

                string classe;
                if (!_classeByName.TryGetValue(name, out classe) || classe == null)
                    classe = "-";

                string signals;
                if (!_signalsByName.TryGetValue(name, out signals) || signals == null)
                    signals = "-";

                ModLog.Info(MARK_RESOLVE + " scena=" + _scene + " nome=\"" + name + "\"" +
                            " classe=" + classe +
                            " chiamate=" + calls +
                            " boss=" + (inBoss ? "si(campo=" + bossField + ")" : "no") +
                            " poolLocale=" + (pool != null ? "si" : "no") +
                            " resources=" + (found != null
                                ? "si(asset=" + (foundAsset ? "si" : "no") + ",attivo=" + (foundActive ? "si" : "no") + ")"
                                : "no") +
                            " segnali=" + signals);
            }
        }

        /// <summary>
        /// L'indice `nome -> GameObject` costruito da `Resources.FindObjectsOfTypeAll<GameObject>()`,
        /// tenuto per scena e ricostruito al massimo ogni `RESOLVE_INDEX_SECONDS`. Serve alla strada 3
        /// di §3.4, quella mai verificata: dice se in un build i prefab sono raggiungibili cosi'.
        /// </summary>
        private static Dictionary<string, GameObject> GetResourcesIndex()
        {
            if (_resourcesIndex != null &&
                Time.realtimeSinceStartup - _resourcesIndexAt < RESOLVE_INDEX_SECONDS)
            {
                return _resourcesIndex;
            }

            GameObject[] all = null;
            try
            {
                all = Resources.FindObjectsOfTypeAll<GameObject>();
            }
            catch (Exception e)
            {
                Fail("Resources.FindObjectsOfTypeAll", e);
            }

            _resourcesIndexAt = Time.realtimeSinceStartup;

            if (all == null)
                return _resourcesIndex;   // si tiene quello di prima, se c'era

            _resourcesIndex = new Dictionary<string, GameObject>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null || string.IsNullOrEmpty(go.name))
                    continue;

                if (!_resourcesIndex.ContainsKey(go.name))
                    _resourcesIndex[go.name] = go;
            }

            return _resourcesIndex;
        }

        // ============================================================================================
        //  RIEPILOGO DI FINE SCENA. La misura non si perde nemmeno se i tetti hanno taciuto le righe.
        // ============================================================================================
        private static void PrintSummary()
        {
            if (_calls == 0 && _censusHolders.Count == 0)
            {
                ModLog.Info(MARK_SUMMARY + " scena=" + _scene +
                            " nessun pool interessante e nessuna chiamata col filtro Weapon|Projectile");
                return;
            }

            List<KeyValuePair<string, int>> sorted = new List<KeyValuePair<string, int>>(_callsByName);
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));

            StringBuilder top = new StringBuilder(MARK_SUMMARY + " top:");
            int shown = sorted.Count < 20 ? sorted.Count : 20;
            for (int i = 0; i < shown; i++)
            {
                top.Append(' ');
                top.Append(sorted[i].Key);
                top.Append('=');
                top.Append(sorted[i].Value);
            }

            if (sorted.Count > shown)
                top.Append(" (+").Append(sorted.Count - shown).Append(" altri)");

            ModLog.Info(MARK_SUMMARY + " scena=" + _scene +
                        " chiamate=" + _calls +
                        " nomi=" + _callsByName.Count +
                        " righeScena=" + _sceneDetailLines +
                        " righeSessione=" + _detailLines +
                        " scenaInSilenzio=" + _sceneFuseReported +
                        " sessioneSpenta=" + _fuseReported +
                        " censimento=" + _censusHolders.Count +
                        " boss=" + _bossDumped.Count +
                        " scartateInCoda=" + _dropped);
            ModLog.Info(top.ToString());
        }

        // ============================================================================================
        //  IL FILTRO. Strutturale, non per scena: la stessa riga che decide il pool nei componenti
        //  d'attacco (`BossJumpAttack.cs:228`). Il risultato e' in cache per prefab.
        // ============================================================================================
        private static bool IsInteresting(GameObject prefab)
        {
            int id;
            try
            {
                id = prefab.GetInstanceID();
            }
            catch (Exception)
            {
                return false;
            }

            bool cached;
            if (_interesting.TryGetValue(id, out cached))
                return cached;

            bool value = false;
            try
            {
                if (FirstWeapon(prefab) != null || FirstProjectile(prefab) != null)
                    value = true;
            }
            catch (Exception)
            {
                value = false;
            }

            _interesting[id] = value;
            return value;
        }

        // ============================================================================================
        //  LETTURE E CLASSIFICAZIONI. Tutte read-only.
        // ============================================================================================
        private static Weapon FirstWeapon(GameObject go)
        {
            if (go == null)
                return null;

            Weapon direct = go.GetComponent<Weapon>();
            if (direct != null)
                return direct;

            Weapon[] all = go.GetComponentsInChildren<Weapon>(true);
            return all.Length > 0 ? all[0] : null;
        }

        private static Projectile FirstProjectile(GameObject go)
        {
            if (go == null)
                return null;

            Projectile direct = go.GetComponent<Projectile>();
            if (direct != null)
                return direct;

            Projectile[] all = go.GetComponentsInChildren<Projectile>(true);
            return all.Length > 0 ? all[0] : null;
        }

        private static string ClassifyEntity(Entity entity)
        {
            if (entity == null)
                return "null";

            Type type = entity.GetType();
            string space = type.Namespace ?? string.Empty;

            if (space.StartsWith("Gameplay.GameControllers.Bosses", StringComparison.Ordinal))
                return "boss:" + type.Name;

            if (space.StartsWith("Gameplay.GameControllers.Enemies", StringComparison.Ordinal))
                return "nemico:" + type.Name;

            if (space.StartsWith("Gameplay.GameControllers.Penitent", StringComparison.Ordinal))
                return "giocatore:" + type.Name;

            return "altro:" + type.Name;
        }

        /// <summary>Chi e' il padrone dell'effetto, letto adesso: arma prima, proiettile poi.</summary>
        private static string OwnerNow(GameObject instance)
        {
            if (instance == null)
                return "nessuna_istanza";

            Weapon weapon = FirstWeapon(instance);
            if (weapon != null)
                return ClassifyEntity(weapon.WeaponOwner);

            Projectile projectile = FirstProjectile(instance);
            if (projectile != null)
                return ClassifyEntity(projectile.owner);

            return "nessuna_arma";
        }

        /// <summary>
        /// Il bersaglio di un proiettile. Se `TargetsPenitent` e' vero il gioco mira a
        /// `Core.Logic.Penitent` LETTO IN LOCALE (`HomingProjectile.cs:199-200`): la copia del peer
        /// mirerebbe al SUO Penitente, che e' il comportamento corretto. Se c'e' un
        /// `AlternativeTarget`, la copia mirerebbe a un oggetto della scena del mittente: quello NON
        /// e' trasportabile. Un proiettile senza homing e' "sparato e dimenticato": vola con la
        /// velocita' scritta dal chiamante dopo `ReuseObject`, che quindi non viaggia col prefab.
        /// </summary>
        private static string AimOf(GameObject instance)
        {
            if (instance == null)
                return "-";

            HomingProjectile homing = null;
            try
            {
                homing = instance.GetComponent<HomingProjectile>();
            }
            catch (Exception)
            {
                return "-";
            }

            if (homing == null)
                return "sparato_e_dimenticato";

            if (homing.TargetsPenitent)
                return "Penitente_locale";

            try
            {
                return homing.AlternativeTarget != null
                    ? "Transform:" + Clean(homing.AlternativeTarget.gameObject.name)
                    : "nessuno";
            }
            catch (Exception)
            {
                return "?";
            }
        }

        private static string ClasseOf(GameObject go, out Weapon weapon, out Projectile projectile)
        {
            weapon = null;
            projectile = null;

            if (go == null)
                return "-";

            try
            {
                weapon = FirstWeapon(go);
                projectile = FirstProjectile(go);
            }
            catch (Exception)
            {
            }

            if (projectile != null)
                return "proiettile";

            if (weapon != null)
                return "arma";

            return "vfx";
        }

        private static bool HasRigidbody(GameObject go)
        {
            if (go == null)
                return false;

            try
            {
                if (go.GetComponentsInChildren<Rigidbody2D>(true).Length > 0)
                    return true;

                if (go.GetComponentsInChildren<Rigidbody>(true).Length > 0)
                    return true;
            }
            catch (Exception)
            {
            }

            return false;
        }

        /// <summary>
        /// I "segnali" per nome: i tipi dei componenti presenti sul prefab. E' la lista che l'utente ha
        /// chiesto (`WallEnemyProjectile`, `Weapon`, `Rigidbody2D`, `Projectile`...) senza doverla
        /// indovinare: si leggono dal prefab vivo.
        /// </summary>
        private static string Segnali(GameObject go, int max)
        {
            if (go == null)
                return "-";

            List<string> names = new List<string>();
            HashSet<string> seen = new HashSet<string>();

            try
            {
                Component[] components = go.GetComponentsInChildren<Component>(true);
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null)
                        continue;

                    Type type = component.GetType();
                    string name = type.Name;

                    if (name == "Transform" || name == "RectTransform")
                        continue;

                    if (seen.Add(name))
                        names.Add(name);
                }
            }
            catch (Exception)
            {
                return "-";
            }

            StringBuilder builder = new StringBuilder();
            int shown = names.Count < max ? names.Count : max;
            for (int i = 0; i < shown; i++)
            {
                if (i > 0)
                    builder.Append('|');

                builder.Append(names[i]);
            }

            if (names.Count > shown)
                builder.Append("|+").Append(names.Count - shown);

            return builder.ToString();
        }

        private static string AttackingEntityOf(GameObject go)
        {
            if (go == null)
                return "-";

            try
            {
                ProjectileWeapon[] weapons = go.GetComponentsInChildren<ProjectileWeapon>(true);
                for (int i = 0; i < weapons.Length; i++)
                {
                    if (weapons[i] != null && weapons[i].AttackingEntity != null)
                        return Clean(weapons[i].AttackingEntity.name);
                }
            }
            catch (Exception)
            {
            }

            return "-";
        }

        // ============================================================================================
        //  RIFLESSIONE: tipi e campi. Cache per tipo, cosi' il costo e' una volta per tipo.
        // ============================================================================================
        private static bool IsReflectable(Component component)
        {
            if (component == null)
                return false;

            string name = component.GetType().Name;
            return name != "Transform" && name != "RectTransform";
        }

        private static string Label(CompRef reference)
        {
            string type = reference.Comp.GetType().Name;
            return string.IsNullOrEmpty(reference.Via) ? type : type + "(" + reference.Via + ")";
        }

        private static Type ElementTypeOf(Type type)
        {
            if (type == null)
                return null;

            if (type.IsArray)
                return type.GetElementType();

            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                if (definition == typeof(List<>) || definition == typeof(IList<>) ||
                    definition == typeof(IEnumerable<>))
                {
                    Type[] arguments = type.GetGenericArguments();
                    if (arguments.Length == 1)
                        return arguments[0];
                }
            }

            return null;
        }

        /// <summary>
        /// Un tipo "dati semplici": una struct o una classe serializzabile che NON e' un oggetto Unity.
        /// E' la forma di `DashAttackInstantiations` (struct, con `prefabToInstantiate` dentro).
        /// </summary>
        private static bool IsPlainData(Type type)
        {
            if (type == null || type == typeof(string) || type == typeof(GameObject))
                return false;

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return false;

            return type.IsValueType || (type.IsClass && type.IsSerializable);
        }

        /// <summary>
        /// True se il tipo porta, a profondita' `depth`, un campo `GameObject`. Serve a non riflettere
        /// su `Vector2`/`LayerMask`/`AnimationCurve` a ogni campo. Cache per tipo, con guardia sui cicli.
        /// </summary>
        private static bool TypeHasGameObject(Type type, int depth)
        {
            if (type == null)
                return false;

            if (type == typeof(GameObject))
                return true;

            bool cached;
            if (_typeHasGo.TryGetValue(type, out cached))
                return cached;

            _typeHasGo[type] = false; // guardia: un tipo che si contiene non ricorre all'infinito

            bool result = false;
            try
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; i++)
                {
                    Type fieldType = fields[i].FieldType;

                    if (fieldType == typeof(GameObject))
                    {
                        result = true;
                        break;
                    }

                    if (depth > 0 && IsPlainData(fieldType) && TypeHasGameObject(fieldType, depth - 1))
                    {
                        result = true;
                        break;
                    }
                }
            }
            catch (Exception)
            {
            }

            _typeHasGo[type] = result;
            return result;
        }

        // ============================================================================================
        //  TESTO
        // ============================================================================================

        /// <summary>
        /// Toglie i suffissi d'ISTANZA, e SOLO quelli: `(Clone)` di Unity e il contatore ` (12)`.
        /// NON toglie le cifre finali di un nome vero (`LandingImpact3`), perche' qui il nome e' quello
        /// del PREFAB e le cifre possono farne parte. Un prefab asset non ha suffissi: se ne compare
        /// uno, e' un'istanza passata al pool, e va ricondotta al nome del prefab.
        /// </summary>
        private static string Clean(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name ?? string.Empty;

            int clone = name.LastIndexOf("(Clone)", StringComparison.Ordinal);
            if (clone > 0)
                name = name.Substring(0, clone).TrimEnd();

            int open = name.LastIndexOf(" (", StringComparison.Ordinal);
            if (open > 0 && name.EndsWith(")", StringComparison.Ordinal))
            {
                string inside = name.Substring(open + 2, name.Length - open - 3);
                if (inside.Length > 0)
                {
                    bool digits = true;
                    for (int i = 0; i < inside.Length; i++)
                    {
                        if (!char.IsDigit(inside[i]))
                        {
                            digits = false;
                            break;
                        }
                    }

                    if (digits)
                        name = name.Substring(0, open).TrimEnd();
                }
            }

            return name;
        }

        /// <summary>Il nome del prefab dietro un figlio del `PoolManager`: `X pool` -> `X`.</summary>
        private static string PrefabOfPoolHolder(string holderName)
        {
            if (string.IsNullOrEmpty(holderName))
                return string.Empty;

            const string suffix = " pool";
            if (holderName.Length > suffix.Length &&
                holderName.EndsWith(suffix, StringComparison.Ordinal))
            {
                return holderName.Substring(0, holderName.Length - suffix.Length);
            }

            return holderName;
        }

        /// <summary>Numero con separatore FISSO: la cultura di questa macchina scrive `0,5`.</summary>
        private static string F(float value)
        {
            return value.ToString("F2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// L'ULTIMA RIGA DI DIFESA, e l'unico `catch` vuoto del file, di proposito: se fallisse anche
        /// `ModLog.Error`, l'eccezione uscirebbe dal punto d'ingresso e arriverebbe DENTRO il gioco.
        /// </summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error(MARK_FAIL + " sonda fallita in " + where +
                             " (nessun effetto sul gioco): " + e.GetType().Name + ": " + e.Message);
            }
            catch
            {
            }
        }

        /// <summary>Una chiamata accodata: tutto cio' che serve a stampare una riga, e nient'altro.</summary>
        private sealed class Call
        {
            public string Name;
            public GameObject Instance;
            public Vector3 Pos;
            public float RotZ;
            public int Frame;
            public int Seq;
            public bool ResultWasNull;
            public string OwnerAtCall;
        }

        /// <summary>Un componente da riflettere, con il campo che lo ha referenziato e la profondita'.</summary>
        private sealed class CompRef
        {
            public readonly Component Comp;
            public readonly string Via;
            public readonly int Depth;

            public CompRef(Component comp, string via, int depth)
            {
                Comp = comp;
                Via = via;
                Depth = depth;
            }
        }
    }
}
