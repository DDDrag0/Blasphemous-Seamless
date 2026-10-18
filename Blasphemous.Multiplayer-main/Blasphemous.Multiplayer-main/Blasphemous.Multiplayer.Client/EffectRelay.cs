using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Entities.Weapon;
using Gameplay.GameControllers.Enemies.Framework.Attack;
using Gameplay.GameControllers.Enemies.Projectiles;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Blasphemous.Multiplayer.Client
{
    // =====================================================================================================
    // ⭐⭐ P1 — IL RELAY DEGLI EFFETTI, GENERICO: **UN HOOK SUL CANALE, E IL FILTRO SUL RICEVENTE**.
    //
    // LA DIAGNOSI CHE LO HA DECISO (`scratch\DIAGNOSI-VOCABOLARIO-EFFETTI.md`):
    //   · il canale di istanziazione del gioco e' UNO, `PoolManager.ReuseObject(prefab, position,
    //     rotation, createPoolIfNeeded, poolSize)`: ci passano gli effetti dei boss (misurato: 2 nomi,
    //     70 e 64 chiamate nella stessa fight) e anche quelli del giocatore, delle trappole e
    //     dell'ambiente (53 pool nella sola stanza del boss);
    //   · il filtro "SONO IO IL PADRONE DEL BOSS?" **oscilla** dentro una fight (80+80 sospensioni del
    //     tree, la proprieta' cambia 3 volte) ⇒ non e' un predicato su cui poggiare un relay;
    //   · il filtro "L'HO **GIA'**?" invece e' **locale, istantaneo e verificabile** ⇒ il doppione
    //     sparisce **per costruzione**, invece di essere evitato sperando che il test vada bene.
    //
    // ⇒ QUINDI. Il mittente **non decide niente di semantico**: manda `prefab (nome) + posizione +
    // rotazione` per OGNI istanziazione che passa dal pool. E' il RICEVENTE a filtrare, con due
    // domande sole, entrambe locali:
    //   ①  **il nome e' un effetto che so ricostruire?** -> la RISOLUZIONE, che ha QUATTRO sorgenti
    //                                                     in ordine (boss, nemici, penitente, cache);
    //   ②  **ce l'ho GIA' / l'ho appena fatto?**         -> la tabella di dedup (prefab + posizione
    //                                                     arrotondata + finestra di 0,5 s).
    // Se la risposta a ① e' no, l'effetto non e' nostro e si salta. Se a ② e' si', l'effetto c'e'
    // gia' su questa macchina (o l'ha appena fatto il gioco stesso) e si salta. Altrimenti lo
    // ISTANZIA **LA SUA COPIA**, dal **suo** pool, con **i suoi** riferimenti.
    //
    // ⭐⭐ LA ① E' STATA ALLARGATA (questo lavoro), e non con tre hook: con UNA strada piu' larga. Una
    // volta il ricevente sapeva rispondere SOLO con i campi del boss locale ("strada 1", misurata
    // 5/5), quindi NON trovava i proiettili dei tre nemici comuni (il `projectilePrefab` sta nei
    // campi del LORO `EnemyAttack`) ne' gli effetti del `Penitent` (stanno nei SUOI campi) — e
    // scriveva `non risolto`, che vuol dire "l'ho ricevuto, ma non so cos'e'".
    // Ora la risoluzione prova in ordine e si ferma alla prima che riesce:
    //   ① i campi della copia locale del BOSS  ② i campi dei NEMICI in scena
    //   ③ i campi del PENITENTE locale         ④ una CACHE per nome
    // e lo DICE: `[FxRelay] RISOLTO "<nome>" da=<boss|nemici|penitente|cache> in=<ms>ms`, oppure
    // `[FxRelay] NON RISOLTO "<nome>" provate: boss,nemici,penitente`. ⇒ **il test e' la misura**:
    // dice quali sorgenti servono, e non serve una sonda separata.
    // ⚠️ E la ricerca larga (②③, riflessione) NON gira per frame NE' a ogni pacchetto: una volta per
    // OGGETTO, e la cache risponde a tutti i pacchetti successivi. E' il costo che va tenuto d'occhio.
    // ⛔ COSA QUESTO FILE NON FA, E NON DEVE FARE: nessun `Kill`, `Damage`, `Death`, `SetHealth(<=0)`,
    // `Init()`, nessuna scrittura di vita, nessun danno iniettato. **L'effetto ferisce da se'**: la
    // copia e' il prefab vero del gioco, istanziato dal pool vero, e la sua `AttackArea` colpisce chi
    // trova su QUESTA macchina con la pipeline del gioco. E' anche il motivo per cui il `[DannoPeer]`
    // del ② resta com'e': le aree continuano a essere scartate dal suo filtro (l'attaccante non e' un
    // `Enemy`, e' l'area), ma ora il danno arriva **dalla copia del ricevente**, non da un pacchetto.
    //
    // ⚠️ E NON TOCCA NIENTE D'ALTRO: nessun `ResetManager()`, nessun `TimeScale`, nessuna camera,
    // nessun `Penitent` dell'altro, nessun collisore. Tutto in `try/catch`: un effetto non deve mai
    // rompere una fight.
    //
    // IL COSTO: **un solo bersaglio Harmony** (`PoolManager.ReuseObject`, un `Postfix`), che sostituisce
    // quello della sonda `6605119` ⇒ i bersagli restano **269**.
    // =====================================================================================================

    /// <summary>
    /// ⭐ IL CANALE — un `Postfix` su `PoolManager.ReuseObject`, cioe' l'unico punto in cui il gioco
    /// crea un effetto da un pool. Il corpo del gioco gira **identico**: qui non si ritorna `false`, non
    /// si salta niente, non si scrive niente. Si legge il prefab che il gioco ha appena riusato e si
    /// passa la palla a <see cref="EffectRelay"/>.
    ///
    /// PERCHE' UN POSTFIX E NON UN PREFIX. Un `Prefix` che ritorna `false` **sostituirebbe** il gioco:
    /// sarebbe un interruttore sugli effetti, cioe' un comportamento, non un relay. Un `Postfix` invece
    /// osserva a cose fatte, e se questo file esplode il gioco ha gia' mostrato il suo effetto.
    ///
    /// ⚠️ I NOMI DEI PARAMETRI SONO CONTRATTUALI: Harmony inietta per NOME contro la firma reale
    /// (`ReuseObject(GameObject prefab, Vector3 position, Quaternion rotation, bool, int32)`), quindi
    /// `prefab` / `position` / `rotation` devono restare scritti cosi'.
    /// </summary>
    [HarmonyPatch(typeof(PoolManager), nameof(PoolManager.ReuseObject))]
    internal static class EffectRelayPoolHook
    {
        [HarmonyPostfix]
        private static void Postfix(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            EffectRelay.OnPoolSpawn(prefab, position, rotation);
        }
    }

    /// <summary>
    /// Il relay vero e proprio: il mittente (<see cref="OnPoolSpawn"/>), il ricevente
    /// (<see cref="ApplyFromWire"/>), il catalogo nome → prefab del boss locale
    /// (<see cref="Resolve"/>) e la tabella di dedup (<see cref="Key"/>).
    /// </summary>
    internal static class EffectRelay
    {
        // =============================================================================================
        //  I MARKER. Tre righe contano, e sono quelle del mandato: quando si **manda**, quando si
        //  **istanzia**, quando si **salta per dedup**. Le altre spiegano un silenzio.
        // =============================================================================================
        internal const string Marker = "[FxRelay]";

        internal const string VerbSend = "mando";             // l'effetto parte verso il peer
        internal const string VerbInstantiate = "istanzio";   // la copia locale nasce da un pacchetto
        internal const string VerbDedup = "salto per dedup";  // la copia c'e' gia' (o e' appena nata)
        internal const string VerbSkip = "non applicato";     // un altro motivo, dichiarato
        internal const string VerbBrake = "freno";            // il tetto di traffico
        internal const string VerbCatalog = "catalogo";       // cosa so risolvere in questa scena

        // ⭐ LA MISURA DELLA RISOLUZIONE. Prima di questo lavoro c'era UNA sola riga, `non risolto`,
        // che diceva «l'ho ricevuto, ma non so cos'è» e non diceva NE' da dove si sarebbe potuto
        // risolverlo NE' quanto era costato cercarlo. Ora la risoluzione ha QUATTRO sorgenti
        // (boss → nemici → penitente → cache) e la riga dice **quale ha vinto** e **in quanti ms**:
        // e' il test a dire quali sorgenti servono, senza una sonda separata.
        internal const string VerbResolved = "RISOLTO";       // il nome ha trovato il suo prefab
        internal const string VerbUnresolved = "NON RISOLTO"; // nessuna delle sorgenti conosce il nome

        /// <summary>
        /// I NOMI DELLE SORGENTI, e sono **contrattuali**: finiscono nel marker (`da=…`) e il test li
        /// legge. `cache` non e' una sorgente: e' la memoria della sorgente che ha vinto.
        /// </summary>
        internal const string SourceBoss = "boss";
        internal const string SourceEnemies = "nemici";
        internal const string SourcePenitent = "penitente";
        internal const string SourceCache = "cache";

        // =============================================================================================
        //  LE COSTANTI, e il perche' di ognuna.
        // =============================================================================================

        /// <summary>
        /// ⭐ LA FINESTRA DEL DEDUP (② del mandato). Mezzo secondo e' **lo stesso ordine di grandezza
        /// della latenza** fra le due macchine e della finestra di invulnerabilita' del `Penitent`
        /// (`RecoverTime = 0.5f`, gia' usata da `EnemyHitPeer`): un effetto identico nato due volte
        /// dentro questa finestra e' lo stesso effetto, non due. Piu' corta e il doppione passa; piu'
        /// lunga e due aree vere consecutive (1,5 unita' l'una dall'altra) si fondono.
        /// </summary>
        private const float DEDUP_SECONDS = 0.5f;

        /// <summary>
        /// ⚠️ IL FRENO. Il canale e' di TUTTI (giocatore compreso), e una preghiera o un ciclo di
        /// particelle possono chiamare il pool decine di volte al secondo. Il numero non e' misurato:
        /// e' scelto sopra il caso peggiore misurato (una summon di ElderBrother e' ~8 aree per
        /// attacco, ~70 per fight) e sotto il punto in cui il traffico diventa un problema.
        /// Quando scatta, lo DICE: un effetto scartato in silenzio sarebbe indistinguibile da un
        /// effetto perso dalla rete.
        /// </summary>
        private const int MAX_SENDS_PER_SECOND = 20;

        /// <summary>
        /// La dimensione del pool quando il ricevente lo deve CREARE (solo allora: se il pool c'e'
        /// gia', questo numero non e' letto). Non e' 1 di proposito: con una sola copia due aree
        /// consecutive riuserebbero lo STESSO oggetto, e la prima sparirebbe dalla scena.
        /// </summary>
        private const int DEFAULT_POOL_SIZE = 4;

        /// <summary>Quanto spesso si ritenta il catalogo quando e' vuoto (il boss nasce dopo la cutscene).</summary>
        private const float CATALOG_FAST_SECONDS = 2f;

        /// <summary>Quanto spesso si rinfresca il catalogo quando ha gia' dei nomi.</summary>
        private const float CATALOG_SLOW_SECONDS = 10f;

        /// <summary>Una riga al secondo per i motivi di scarto: non sono la misura, sono la spiegazione.</summary>
        private const float LOG_INTERVAL = 1f;

        /// <summary>Oltre questa dimensione la tabella del dedup si ripulisce delle voci scadute.</summary>
        private const int SEEN_SWEEP_AT = 512;

        private const int MAX_CATALOG_NAMES_LOGGED = 12;

        /// <summary>Quanti componenti del boss si riflettono al massimo (una guardia, non una misura).</summary>
        private const int MAX_BOSS_COMPONENTS = 96;

        /// <summary>
        /// ⚠️ IL TETTO DELLA RICERCA LARGA — e sono due tetti, perche' le due sorgenti larghe hanno
        /// forme diverse.
        ///
        /// `MAX_ENEMY_COMPONENTS` (per nemico): un nemico comune non e' un boss. Il campo che serve
        /// (`projectilePrefab`) sta su un `EnemyAttack` che il nemico si porta addosso, cioe' fra i
        /// primi componenti: 64 lascia dentro anche i nemici piu' componibili, e taglia i prefab di
        /// scena enormi (il `RangedBoomerang` non lo e').
        ///
        /// `MAX_PENITENT_COMPONENTS`: il `Penitent` e' l'oggetto piu' grosso del gioco (abilita',
        /// preghiere, audio, animator, effetti). 128 e' il doppio del boss perche' gli effetti del
        /// giocatore vivono negli stessi componenti delle sue abilita', e tagliare li' vorrebbe dire
        /// non risolvere proprio i nomi per cui esiste la sorgente ③.
        /// </summary>
        private const int MAX_ENEMY_COMPONENTS = 64;
        private const int MAX_PENITENT_COMPONENTS = 128;

        /// <summary>
        /// Quanti NEMICI si scandiscono in un solo tentativo. Non e' una misura: e' un tetto perche'
        /// una stanza con cinquanta nemici non deve fare cinquanta riflessioni dentro un `Postfix`.
        /// Ogni nemico si scandisce UNA VOLTA per scena (i nomi trovati restano in cache), quindi una
        /// stanza affollata si completa in due tentativi, e il secondo arriva dopo
        /// <see cref="WIDE_RETRY_SECONDS"/>.
        /// </summary>
        private const int MAX_ENEMIES_PER_SEARCH = 32;

        /// <summary>
        /// ⚠️ OGNI QUANTO SI RITENTA UN NOME CHE NESSUNA SORGENTE HA TROVATO. E' il numero che tiene
        /// vera la frase «la ricerca larga si fa **una volta per nome**»: positivo ⇒ la cache risponde
        /// e non si cerca piu'; negativo ⇒ non si cerca a ogni pacchetto (che con una raffica di
        /// proiettili sarebbe una scansione di riflessione per proiettile), ma nemmeno si rinuncia per
        /// SEMPRE, perche' il primo tentativo puo' arrivare prima che il nemico che ha sparato sia in
        /// scena su questa macchina. Il costo e' dichiarato: al massimo una ricerca larga per nome
        /// ogni 10 secondi.
        /// </summary>
        private const float WIDE_RETRY_SECONDS = 10f;

        // =============================================================================================
        //  STATO. Tutto per scena; `_interesting` e `_typeHasGo` sono cache di TIPO e non si azzerano
        //  (un prefab e' un asset: la risposta non cambia).
        // =============================================================================================
        private static string _scene = string.Empty;
        private static bool _inLevel;

        /// <summary>⭐ LA TABELLA DEL DEDUP: chiave (<see cref="Key"/>) → istante in cui l'ho vista.</summary>
        private static readonly Dictionary<string, float> _seen = new Dictionary<string, float>();

        /// <summary>⭐ IL CATALOGO: nome del prefab → riferimento LOCALE, preso dai campi del boss.</summary>
        private static readonly Dictionary<string, GameObject> _catalog = new Dictionary<string, GameObject>();

        /// <summary>
        /// ⭐⭐ LA CACHE (sorgente ④): nome → prefab risolto da una sorgente LARGA (② nemici o ③
        /// penitente). Non contiene i nomi del boss: quelli stanno gia' in <see cref="_catalog"/>, che
        /// e' il primo posto in cui si guarda. Vive per scena.
        ///
        /// ⚠️ E non contiene solo il nome CERCATO: la riflessione su un nemico restituisce **tutti** i
        /// suoi prefab, e si mettono in cache tutti. E' cio' che rende il costo «una volta per
        /// OGGETTO» e non «una volta per nome»: il secondo proiettile dello stesso nemico arriva con
        /// `da=cache` e costa zero.
        /// </summary>
        private static readonly Dictionary<string, GameObject> _cache = new Dictionary<string, GameObject>();

        /// <summary>Quando un nome ha fallito TUTTE le sorgenti: e' il tetto della ricerca larga.</summary>
        private static readonly Dictionary<string, float> _missAt = new Dictionary<string, float>();

        /// <summary>I nemici gia' scanditi in questa scena (per `GetInstanceID`, come `_interesting`).</summary>
        private static readonly HashSet<int> _harvested = new HashSet<int>();

        /// <summary>I nomi gia' detti una volta come "non risolti": una riga per nome per scena.</summary>
        private static readonly HashSet<string> _unresolved = new HashSet<string>();

        /// <summary>I nomi gia' detti una volta come RISOLTI (e da quale sorgente): una riga per nome.</summary>
        private static readonly HashSet<string> _reported = new HashSet<string>();

        /// <summary>
        /// Il cronometro della RISOLUZIONE. E' la misura del mandato, e sta qui e non nel chiamante
        /// perche' deve misurare la ricerca e nient'altro (nessuna serializzazione, nessun log).
        /// </summary>
        private static readonly System.Diagnostics.Stopwatch _timer = new System.Diagnostics.Stopwatch();

        /// <summary>Il filtro strutturale del mittente, in cache per prefab.</summary>
        private static readonly Dictionary<int, bool> _interesting = new Dictionary<int, bool>();

        /// <summary>Quali tipi portano un campo `GameObject`, in cache per tipo.</summary>
        private static readonly Dictionary<Type, bool> _typeHasGo = new Dictionary<Type, bool>();

        /// <summary>
        /// ⚠️ I CAMPI DI UN TIPO, in cache per TIPO. Prima questa `GetFields` girava a ogni componente
        /// di ogni scansione: con quattro sorgenti e tutti i nemici della scena sarebbe diventato il
        /// costo dominante. I campi di un tipo non cambiano a runtime ⇒ si leggono una volta sola.
        /// </summary>
        private static readonly Dictionary<Type, FieldInfo[]> _fieldsOfType = new Dictionary<Type, FieldInfo[]>();

        /// <summary>
        /// ⚠️ IL GUARDIA DI RIENTRANZA. Quando istanzio la copia di un effetto ricevuto, il mio
        /// `Postfix` scatta **anche lui** (sto chiamando `ReuseObject`). Senza questa bandiera il
        /// pacchetto rimbalzerebbe al mittente, e per un attimo esisterebbe un anello fra le due
        /// macchine che si spegne solo grazie al dedup. Con la bandiera l'anello **non esiste**.
        /// </summary>
        private static bool _applyingFromWire;

        private static float _catalogAt = -1000f;
        private static bool _catalogHasNames;
        private static string _catalogLogged = string.Empty;

        private static int _sendWindowCount;
        private static float _sendWindowAt = -1000f;

        private static float _lastBrakeLog = -1000f;
        private static float _lastSkipLog = -1000f;

        // ---- LE SORGENTI LARGHE (② nemici, ③ penitente) ---------------------------------------------
        /// <summary>Il `Penitent` locale si scandisce UNA volta per scena: e' uno solo e non cambia.</summary>
        private static bool _penitentHarvested;

        // ---- I CONTATORI DELLA MISURA (finiscono nel log e nel referto) ------------------------------
        private static int _sends;
        private static int _instantiated;
        private static int _deduped;
        private static int _unresolvedCount;
        private static int _braked;

        /// <summary>
        /// Quante risoluzioni hanno vinto, per sorgente: `boss`, `nemici`, `penitente`, `cache`.
        /// </summary>
        private static readonly Dictionary<string, int> _bySource = new Dictionary<string, int>();

        /// <summary>
        /// ⭐ QUANTI NOMI OGNI SORGENTE HA PORTATO — la seconda metà della misura, e la più utile da
        /// leggere: dice se una sorgente serve (porta nomi) o se è costo senza risultati (zero nomi).
        /// </summary>
        private static readonly Dictionary<string, int> _namesBySource = new Dictionary<string, int>();

        /// <summary>Le risoluzioni misurate: quante, il totale e il peggiore (ms).</summary>
        private static int _resolves;
        private static double _resolveTotalMs;
        private static double _resolveMaxMs;

        /// <summary>Quante volte la ricerca LARGA e' stata davvero eseguita, e quante volte saltata dal tetto.</summary>
        private static int _wideSearches;
        private static int _wideSkipped;

        /// <summary>Quanti nemici sono stati riflessi in questa scena (la voce "costo" del referto).</summary>
        private static int _enemiesScanned;

        // =============================================================================================
        //  CICLO DI VITA. Chiamati da `Multiplayer.cs` (OnLevelLoaded / OnLateUpdate): nessun
        //  `MonoBehaviour` nuovo e nessun bersaglio Harmony nuovo.
        // =============================================================================================

        /// <summary>
        /// Una scena nuova: il catalogo e il dedup sono di QUELLA scena, e vecchi riferimenti a
        /// prefab di un livello scaricato sarebbero distruttori (`GameObject` distrutti).
        /// </summary>
        internal static void LevelLoaded(string level)
        {
            try
            {
                if (!string.IsNullOrEmpty(_scene) && _scene != level)
                    PrintSummary();

                _scene = level ?? string.Empty;
                _inLevel = !string.IsNullOrEmpty(_scene) && _scene != "MainMenu";

                _seen.Clear();
                _catalog.Clear();
                _cache.Clear();
                _missAt.Clear();
                _harvested.Clear();
                _unresolved.Clear();
                _reported.Clear();
                _catalogAt = -1000f;
                _catalogHasNames = false;
                _catalogLogged = string.Empty;
                _sendWindowCount = 0;
                _sendWindowAt = -1000f;
                _applyingFromWire = false;
                _penitentHarvested = false;

                _sends = 0;
                _instantiated = 0;
                _deduped = 0;
                _unresolvedCount = 0;
                _braked = 0;
                _bySource.Clear();
                _namesBySource.Clear();
                _resolves = 0;
                _resolveTotalMs = 0d;
                _resolveMaxMs = 0d;
                _wideSearches = 0;
                _wideSkipped = 0;
                _enemiesScanned = 0;

                if (_inLevel)
                {
                    ModLog.Info($"{Marker} armato scena={_scene} finestraDedup={DEDUP_SECONDS:F1}s " +
                                $"freno={MAX_SENDS_PER_SECOND}/s (il filtro e' sul RICEVENTE: " +
                                $"risoluzione nome→prefab da {SourceBoss}|{SourceEnemies}|" +
                                $"{SourcePenitent}|{SourceCache} + dedup)");
                }

                // `_interesting`, `_typeHasGo` e `_fieldsOfType` NON si azzerano: riguardano gli ASSET
                // (e i TIPI), non la scena.
            }
            catch (Exception e)
            {
                Fail("LevelLoaded", e);
            }
        }

        /// <summary>
        /// Il ciclo per frame. Serve a UNA cosa: armare il catalogo appena il boss esiste davvero
        /// (nasce dopo la cutscene, quindi non c'e' al caricamento del livello) e rinfrescarlo ogni
        /// tanto. Non manda, non istanzia, non tocca niente.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                if (!_inLevel)
                    return;

                float interval = _catalogHasNames ? CATALOG_SLOW_SECONDS : CATALOG_FAST_SECONDS;
                if (Time.unscaledTime - _catalogAt < interval)
                    return;

                BuildCatalog();
            }
            catch (Exception e)
            {
                Fail("Tick", e);
            }
        }

        // =============================================================================================
        //  ① IL MITTENTE. Una riga di decisione, e nessuna semantica: manda il NOME, la POSIZIONE e la
        //  ROTAZIONE di cio' che il gioco ha appena preso dal pool.
        // =============================================================================================

        /// <summary>
        /// Chiamata dal `Postfix`. Non lancia mai: se il relay non parte, il gioco ha comunque fatto
        /// il suo effetto (questa e' l'unica proprieta' che conta qui).
        /// </summary>
        internal static void OnPoolSpawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            try
            {
                // La copia di un effetto RICEVUTO non si rimanda indietro: vedi `_applyingFromWire`.
                if (_applyingFromWire || prefab == null)
                    return;

                // ⚠️ IL FILTRO DEL MITTENTE E' SOLO SUL TRAFFICO, non sul significato. Passano "cio'
                // che ferisce" (una `Weapon`) e "cio' che vola" (un `Projectile`): sono le due
                // categorie che il relay ha senso di trasmettere, e sono la stessa riga con cui il
                // gioco sceglie il pool nei suoi componenti d'attacco. I cosmetici puri restano fuori
                // per non inondare il canale — NON perche' siano sbagliati: la scelta se un effetto
                // vale la pena e' del RICEVENTE, che decide col catalogo.
                if (!IsInteresting(prefab))
                    return;

                string name = Clean(prefab.name);
                if (string.IsNullOrEmpty(name))
                    return;

                // ⭐ LA MIA COPIA ENTRA NEL DEDUP **PRIMA** DI OGNI ALTRA COSA, e non e' un dettaglio:
                // e' cio' che fa funzionare il filtro del ricevente. Quando il peer mi manda lo stesso
                // effetto nello stesso posto, la chiave e' gia' qui e il pacchetto si salta. Vale
                // anche quando l'effetto l'ho prodotto io (fight in cui entrambe le macchine simulano)
                // e anche quando a produrlo e' stato un altro canale della mod (vedi NoteLocalSpawn).
                Note(name, position);

                if (!_inLevel)
                    return;

                if (Main.Multiplayer == null || Main.Multiplayer.NetworkManager == null ||
                    !Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                // Se nessun altro giocatore e' in QUESTA scena, il server non inoltrerebbe niente:
                // il pacchetto sarebbe solo traffico locale. Il predicato e' quello che il mod usa gia'
                // per la bossfight (`[BossJoin]`), non uno nuovo.
                if (!OtherPlayerHere())
                    return;

                if (Braked(name))
                    return;

                byte[] payload = BuildPayload(name, position, rotation.eulerAngles.z);
                Main.Multiplayer.NetworkManager.SendAttackPayload(payload);

                _sends++;
                ModLog.Info($"{Marker} {VerbSend} n={_sends} prefab=\"{name}\" pos={Pos(position)} " +
                            $"rotZ={F(rotation.eulerAngles.z)} (canale Attack, tipo " +
                            $"{(byte)AttackType.CoopEffectRelay})");
            }
            catch (Exception e)
            {
                Fail("OnPoolSpawn", e);
            }
        }

        /// <summary>
        /// ⭐ L'ANCORA DEL CANALE ESISTENTE. La mod ha gia' una via per gli effetti del giocatore
        /// (`EffectType`, un enum chiuso: polvere della scivolata, polvere del dash, impatto a terra).
        /// Quei punti istanziano dal pool come tutti gli altri, quindi il `Postfix` li vedrebbe — ma
        /// **solo se passassero il filtro `Weapon|Projectile`, e non lo passano**: sono cosmetici puri.
        /// Questa chiamata li registra comunque nella **stessa** tabella del dedup, cosi' la tabella
        /// copre **entrambe le vie** per costruzione e non per coincidenza: se un domani il filtro si
        /// allarga (o un cosmetico acquista una `Weapon`), vince il primo che arriva e il secondo si
        /// salta. Oggi il caso non e' esercitabile, ed e' dichiarato nel referto.
        /// </summary>
        internal static void NoteLocalSpawn(GameObject prefab, Vector3 position)
        {
            try
            {
                if (prefab == null)
                    return;

                string name = Clean(prefab.name);
                if (string.IsNullOrEmpty(name))
                    return;

                Note(name, position);
            }
            catch (Exception e)
            {
                Fail("NoteLocalSpawn", e);
            }
        }

        /// <summary>
        /// Il freno: al massimo <see cref="MAX_SENDS_PER_SECOND"/> invii per secondo. Ritorna `true`
        /// quando il pacchetto va **scartato**, e lo dice una volta al secondo.
        ///
        /// ⚠️ Un effetto scartato qui e' un effetto che il peer **non vede**: e' il costo dichiarato
        /// del freno. La scelta e' deliberata — meglio perdere il decimo effetto di una raffica che
        /// saturare la connessione durante una fight — ma il numero e' un tetto, non una stima.
        /// </summary>
        private static bool Braked(string name)
        {
            float now = Time.unscaledTime;

            if (now - _sendWindowAt >= 1f)
            {
                _sendWindowAt = now;
                _sendWindowCount = 0;
            }

            if (_sendWindowCount < MAX_SENDS_PER_SECOND)
            {
                _sendWindowCount++;
                return false;
            }

            _braked++;
            LogThrottled(ref _lastBrakeLog, $"{Marker} {VerbBrake}: gia' {MAX_SENDS_PER_SECOND} invii " +
                                            $"in questo secondo, scarto (ultimo prefab=\"{name}\", " +
                                            $"scartati finora={_braked})");
            return true;
        }

        // =============================================================================================
        //  ② IL RICEVENTE — il filtro. Due domande locali, nell'ordine che conta.
        // =============================================================================================

        /// <summary>
        /// Un effetto e' arrivato dal canale `Attack`. `startIdx` e' l'indice del byte del tipo
        /// (cioe' subito dopo il nome del mittente che il server premette).
        ///
        /// LA FORMA DEL PACCHETTO (auto-descritta dal primo byte, quindi nessuna riga di protocollo e
        /// nessuna riga di server — il payload e' minuscolo e ci sta comodamente: il frame e' `ushort`,
        /// quindi 65535 byte, e qui ne bastano ~35):
        /// <code>
        ///   +0   byte    AttackType.CoopEffectRelay (201)
        ///   +1   byte    lunghezza del nome del prefab (UTF8)
        ///   +2   string  nome del prefab
        ///   ..   float   x, y, z            (la posizione VERA, non arrotondata)
        ///   ..   float   rotZ               (la rotazione come angolo Z: e' l'unica che il gioco usa)
        /// </code>
        /// ⚠️ La posizione viaggia **non arrotondata** e si arrotonda solo nella CHIAVE del dedup:
        /// disegnare l'effetto a un intero lo sposterebbe di mezzo mondo rispetto al boss, e la chiave
        /// invece ha bisogno di essere **identica** su due macchine che misurano lo stesso evento.
        /// </summary>
        internal static void ApplyFromWire(string senderName, byte[] data, int startIdx)
        {
            try
            {
                if (data == null || startIdx < 0 || startIdx + 2 > data.Length)
                {
                    SkipThrottled("pacchetto troppo corto (" +
                                  (data == null ? "null" : data.Length.ToString()) + " byte)");
                    return;
                }

                int nameLength = data[startIdx + 1];
                int index = startIdx + 2;

                if (data.Length < index + nameLength + 16)
                {
                    SkipThrottled("pacchetto troncato (nome " + nameLength + " byte, " +
                                  (data.Length - index) + " disponibili)");
                    return;
                }

                string name = Encoding.UTF8.GetString(data, index, nameLength);
                index += nameLength;

                float x = BitConverter.ToSingle(data, index);
                float y = BitConverter.ToSingle(data, index + 4);
                float z = BitConverter.ToSingle(data, index + 8);
                float rotZ = BitConverter.ToSingle(data, index + 12);

                if (string.IsNullOrEmpty(name))
                {
                    SkipThrottled("nome del prefab vuoto");
                    return;
                }

                if (!_inLevel)
                {
                    SkipThrottled("fuori da un livello (menu o cambio di scena)");
                    return;
                }

                Vector3 position = new Vector3(x, y, z);

                // ---- ① IL NOME, QUALUNQUE SORGENTE LO CONOSCA --------------------------------------
                //
                // QUATTRO SORGENTI IN ORDINE, si ferma alla prima che riesce (vedi `Resolve`):
                //   ① i campi del MIO boss   — la strada 1, l'unica MISURATA (5/5 nomi del boss)
                //   ② i campi dei NEMICI     — i proiettili dei 3 nemici (`projectilePrefab` sta li')
                //   ③ i campi del PENITENTE  — gli effetti del giocatore
                //   ④ la CACHE per nome      — trovato una volta, si ricorda
                // E il marker dice **quale ha vinto** e **in quanti ms**: e' la misura del mandato.
                string source;
                double resolveMs;
                GameObject prefab = Resolve(name, out source, out resolveMs);

                if (prefab == null)
                {
                    // Non e' un errore: e' un effetto che su QUESTA macchina non so ricostruire (un
                    // proiettile del giocatore, una trappola, un boss che qui non c'e'). UNA riga per
                    // nome per scena — e dice le sorgenti PROVATE, cosi' il test sa cosa manca.
                    _unresolvedCount++;
                    if (_unresolved.Add(name))
                    {
                        string penitentState = _penitentHarvested ? "si" : "no";

                        ModLog.Info($"{Marker} {VerbUnresolved} \"{name}\" provate: " +
                                    $"{SourceBoss},{SourceEnemies},{SourcePenitent} mittente={senderName} " +
                                    $"(in={F3(resolveMs)}ms, catalogo {SourceBoss}={_catalog.Count} nomi, " +
                                    $"{SourceEnemies} scanditi={_enemiesScanned}, {SourcePenitent}={penitentState}, " +
                                    $"cache={_cache.Count}, non risolti finora={_unresolvedCount})");
                    }

                    return;
                }

                // ⭐ LA RIGA DELLA MISURA: una per nome, la prima volta. Chi legge il log sa DA DOVE
                // viene il prefab e QUANTO e' costato trovarlo, senza una sonda separata.
                //
                // ⚠️ E il blocco ha il SUO `try/catch`, che sembra eccessivo e non lo e': qui siamo
                // DOPO la risoluzione e PRIMA della copia, quindi un'eccezione nel log butterebbe via
                // un effetto che si poteva istanziare. Un marker non deve mai costare un effetto.
                try
                {
                    if (_reported.Add(name))
                    {
                        int seen;
                        _bySource.TryGetValue(source, out seen);
                        _bySource[source] = seen + 1;

                        ModLog.Info($"{Marker} {VerbResolved} \"{name}\" da={source} in={F3(resolveMs)}ms " +
                                    $"mittente={senderName} (catalogo {SourceBoss}={_catalog.Count} nomi, " +
                                    $"{SourceEnemies} scanditi={_enemiesScanned}, cache={_cache.Count}, " +
                                    $"risolti finora={_reported.Count})");
                    }
                }
                catch (Exception e)
                {
                    Fail("riga RISOLTO", e);
                }

                // ---- ② CE L'HO GIA'? --------------------------------------------------------------
                //
                // La chiave e' `prefab + posizione arrotondata`, con la convenzione del progetto
                // (`x:F0` / `y:F0`), e la finestra e' di mezzo secondo. Se la chiave c'e' ed e'
                // recente, **l'effetto esiste gia' su questa macchina**: o l'ho appena istanziato io
                // (fight in cui entrambe le macchine simulano) o l'ha istanziato il gioco per conto
                // suo. In entrambi i casi una seconda copia sarebbe IL doppione, e si salta.
                string key = Key(name, position);
                float when;
                if (_seen.TryGetValue(key, out when))
                {
                    float age = Time.unscaledTime - when;
                    if (age <= DEDUP_SECONDS)
                    {
                        _deduped++;
                        ModLog.Info($"{Marker} {VerbDedup} prefab=\"{name}\" pos={Pos(position)} " +
                                    $"eta={F(age)}s da={senderName} (la copia ce l'ho gia': " +
                                    $"dedup totale={_deduped})");
                        return;
                    }
                }

                // ---- ③ LA SUA COPIA --------------------------------------------------------------
                PoolManager pool = PoolManager.Instance;
                if (pool == null)
                {
                    SkipThrottled("PoolManager assente in questa scena (la scena sta cambiando?)");
                    return;
                }

                PoolManager.ObjectInstance instance;
                _applyingFromWire = true;
                try
                {
                    // ⚠️ `createPoolIfNeeded: true` NON e' una comodita': se il pool non c'e' ancora,
                    // `ReuseObject` restituirebbe **null** e l'effetto non esisterebbe (e' il caso in
                    // cui il boss di questa macchina non ha ancora fatto il suo `CreatePool`).
                    // ⚠️ E il riuso passa da `ObjectInstance.Reuse` -> `PoolObject.OnObjectReuse`, che
                    // e' **obbligatorio**: e' li' che `Animator`, stato e tempi vengono riportati a
                    // nuovo. Un effetto del pool **si riaccende**, non si ricrea.
                    instance = pool.ReuseObject(prefab, position, Quaternion.Euler(0f, 0f, rotZ),
                                                true, DEFAULT_POOL_SIZE);
                }
                finally
                {
                    _applyingFromWire = false;
                }

                if (instance == null)
                {
                    SkipThrottled("il pool non ha restituito l'istanza per \"" + name + "\"");
                    return;
                }

                Note(name, position);

                _instantiated++;
                ModLog.Info($"{Marker} {VerbInstantiate} prefab=\"{name}\" pos={Pos(position)} " +
                            $"rotZ={F(rotZ)} da={senderName} (copia locale dal mio pool: da qui in poi " +
                            $"l'effetto e' di QUESTA macchina, danno compreso; istanziati={_instantiated})");
            }
            catch (Exception e)
            {
                // L'ultimo `finally` ha gia' rialzato la bandiera di rientranza: se qualcosa e' andato
                // storto qui, il relay resta muto per questo pacchetto e il gioco non se ne accorge.
                _applyingFromWire = false;
                Fail("ApplyFromWire", e);
            }
        }

        // =============================================================================================
        //  IL PACCHETTO
        // =============================================================================================

        /// <summary>
        /// Il payload del canale `Attack`, tipo <see cref="AttackType.CoopEffectRelay"/>. La forma e'
        /// commentata in <see cref="ApplyFromWire"/>.
        /// </summary>
        private static byte[] BuildPayload(string name, Vector3 position, float rotZ)
        {
            byte[] nameBytes = Encoding.UTF8.GetBytes(name);
            if (nameBytes.Length > 255)
                Array.Resize(ref nameBytes, 255);

            byte[] payload = new byte[2 + nameBytes.Length + 16];

            payload[0] = (byte)AttackType.CoopEffectRelay;
            payload[1] = (byte)nameBytes.Length;
            Buffer.BlockCopy(nameBytes, 0, payload, 2, nameBytes.Length);

            int index = 2 + nameBytes.Length;
            Buffer.BlockCopy(BitConverter.GetBytes(position.x), 0, payload, index, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(position.y), 0, payload, index + 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(position.z), 0, payload, index + 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(rotZ), 0, payload, index + 12, 4);

            return payload;
        }

        // =============================================================================================
        //  ③ LA RISOLUZIONE nome → prefab: QUATTRO SORGENTI IN ORDINE, si ferma alla prima che riesce.
        //
        //   ① i campi della COPIA LOCALE DEL BOSS   (la strada 1, MISURATA 5/5) -> `_catalog`
        //   ② i campi dei NEMICI IN SCENA           (i proiettili dei 3 nemici)  -> `_cache`
        //   ③ i campi del PENITENTE LOCALE          (gli effetti del giocatore)  -> `_cache`
        //   ④ la CACHE per nome                    (trovato una volta, si ricorda)
        //
        // ⚠️ PERCHE' QUESTO ORDINE, E NON UN ALTRO: la ① e la ④ sono due lookup in un dizionario
        // (decine di nanosecondi); la ② e la ③ sono RIFLESSIONE (millisecondi). ⇒ la ricerca larga si
        // paga SOLO per i nomi che oggi finivano in `non risolto`, e UNA VOLTA per oggetto: la cache
        // della ④ risponde a tutti i pacchetti successivi. La ricerca larga NON gira per frame — non
        // gira nemmeno a ogni pacchetto, perché un nome già trovato non si cerca più.
        //
        // ⚠️ E «VINCE LA PRIMA»: se due sorgenti conoscono lo stesso nome, il prefab è quello della
        // sorgente che viene PRIMA (boss > nemici > penitente > cache). Non è un pareggio da
        // arbitrare: due prefab con lo stesso nome in due posti diversi sono due asset diversi, e
        // l'ordine è la regola. Il marker lo dichiara, perché scrive `da=`.
        // =============================================================================================

        /// <summary>
        /// Il nome arriva dal peer, il riferimento lo dà questa macchina. Prova le sorgenti in ordine
        /// e si ferma alla prima che riesce; dice **quale** è stata (<paramref name="source"/>) e
        /// **quanto è costato** (<paramref name="ms"/>). Non lancia mai: una risoluzione non deve
        /// rompere un effetto.
        /// </summary>
        private static GameObject Resolve(string name, out string source, out double ms)
        {
            source = null;
            ms = 0d;

            _timer.Reset();
            _timer.Start();

            GameObject prefab;
            try
            {
                prefab = ResolveCore(name, ref source);
            }
            catch (Exception e)
            {
                // L'ultima difesa: se la riflessione esplode, il nome resta non risolto e il gioco
                // non se ne accorge (l'effetto del peer non nasce, come prima di questo lavoro).
                Fail("Resolve", e);
                prefab = null;
            }

            _timer.Stop();
            ms = _timer.Elapsed.TotalMilliseconds;

            // ⭐ LA MISURA VA CONTATA SEMPRE, anche sui fallimenti: `_resolveMaxMs` è il numero che
            // dice quanto è costata la risoluzione peggiore, ed è quello che finisce nel riepilogo.
            _resolves++;
            _resolveTotalMs += ms;
            if (ms > _resolveMaxMs)
                _resolveMaxMs = ms;

            return prefab;
        }

        /// <summary>
        /// Il corpo della risoluzione, con le sue uscite anticipate. `ref` invece di `out` perché ogni
        /// sorgente che vince deve poter scrivere la propria etichetta.
        /// </summary>
        private static GameObject ResolveCore(string name, ref string source)
        {
            GameObject prefab;

            // ---- ① I CAMPI DEL BOSS ------------------------------------------------------------
            //
            // La strada 1 dello studio, l'unica MISURATA (5/5 nomi del boss): il riferimento al
            // prefab sta nei campi serializzati della copia LOCALE del boss, e si legge per
            // riflessione. Niente `GameObject.Find`, e soprattutto niente
            // `Resources.FindObjectsOfTypeAll`: quella risolve sì 53 nomi su 53, ma per `LandingFx`
            // restituisce l'istanza di scena e non l'asset (`asset=no`), cioè un catalogo che sembra
            // completo e ha un buco silenzioso proprio su un prefab del boss.
            if (_catalog.TryGetValue(name, out prefab) && prefab != null)
            {
                source = SourceBoss;
                return prefab;
            }

            // Il nome non c'è: o il catalogo è vecchio, o il boss qui non ha quel campo. Si ritenta
            // con un tetto di tempo, perché scandire la scena a ogni pacchetto sarebbe un costo
            // dentro una fight. ⚠️ LA CADENZA È QUELLA DI PRIMA (2 s a catalogo vuoto, 10 s quando
            // ha già dei nomi): questa sorgente non è stata toccata da questo lavoro.
            if (Time.unscaledTime - _catalogAt >= CATALOG_FAST_SECONDS)
            {
                BuildCatalog();

                if (_catalog.TryGetValue(name, out prefab) && prefab != null)
                {
                    source = SourceBoss;
                    return prefab;
                }
            }

            // ---- ④ LA CACHE (prima delle sorgenti LARGHE: è il suo unico scopo) ----------------
            if (_cache.TryGetValue(name, out prefab))
            {
                if (prefab != null)
                {
                    source = SourceCache;
                    return prefab;
                }

                _cache.Remove(name); // l'asset è stato scaricato: la voce non vale più
            }

            // ⚠️ IL TETTO DELLA RICERCA LARGA. Un nome che nessuna sorgente conosce verrebbe cercato a
            // OGNI pacchetto, e una raffica di proiettili non risolti diventerebbe una scansione di
            // riflessione per proiettile. Il nome si ritenta al massimo ogni `WIDE_RETRY_SECONDS`:
            // è il numero che tiene vera la frase «una volta per nome» anche quando il nome NON si
            // trova, e che lascia una porta aperta al nemico non ancora in scena al primo tentativo.
            float now = Time.unscaledTime;
            float missedAt;
            if (_missAt.TryGetValue(name, out missedAt) && now - missedAt < WIDE_RETRY_SECONDS)
            {
                _wideSkipped++;
                return null;
            }

            // ---- ② I CAMPI DEI NEMICI, poi ---- ③ I CAMPI DEL PENITENTE ------------------------
            _wideSearches++;

            if (SearchEnemies(name, out prefab))
            {
                source = SourceEnemies;
                return prefab;
            }

            if (SearchPenitent(name, out prefab))
            {
                source = SourcePenitent;
                return prefab;
            }

            _missAt[name] = now;
            return null;
        }

        // =============================================================================================
        //  ② I CAMPI DEI NEMICI. «Quali nemici?» — TUTTI quelli in scena, attivi e non: il prefab del
        //  proiettile sta su un `EnemyAttack` che il nemico si porta addosso (`projectilePrefab`), e
        //  non c'è bisogno che quel nemico sia tracciato dal sync perché il RIFERIMENTO ci sia.
        //  ⚠️ I boss si saltano: quelli sono la ① e rifletterli qui sarebbe pagare due volte la cosa
        //  più grossa della scena.
        // =============================================================================================
        private static bool SearchEnemies(string name, out GameObject found)
        {
            found = null;

            Enemy[] all;
            try
            {
                all = EnemySyncManager.AllEnemiesIncludingInactive();
            }
            catch (Exception e)
            {
                Fail("SearchEnemies", e);
                return false;
            }

            if (all == null)
                return false;

            int scanned = 0;
            for (int i = 0; i < all.Length && scanned < MAX_ENEMIES_PER_SEARCH; i++)
            {
                Enemy enemy = all[i];
                if (enemy == null)
                    continue;

                if (IsBossType(enemy))
                    continue;

                // Già scandito in questa scena: i suoi nomi sono già nella cache, e rileggerli non
                // aggiungerebbe niente. È questo — non il tetto dei nemici — che rende il costo
                // «una volta per oggetto».
                int id;
                try
                {
                    id = enemy.GetInstanceID();
                }
                catch (Exception)
                {
                    continue;
                }

                if (!_harvested.Add(id))
                    continue;

                scanned++;
                _enemiesScanned++;

                try
                {
                    Harvest(enemy, MAX_ENEMY_COMPONENTS, _cache, SourceEnemies);
                }
                catch (Exception e)
                {
                    Fail("Harvest/Enemy", e);
                }

                if (_cache.TryGetValue(name, out found) && found != null)
                    return true;
            }

            return false;
        }

        // =============================================================================================
        //  ③ I CAMPI DEL PENITENTE. `Core.Logic.Penitent` è il giocatore LOCALE: l'altro non è un
        //  `Penitent`, è un avatar nudo (`OtherPlayerScript`), quindi non c'è un «Penitente remoto»
        //  da scandire — e non serve: i prefab che il relay deve ricostruire sono ASSET del gioco,
        //  e i riferimenti serializzati sul MIO `Penitent` sono gli stessi che ha il suo.
        // =============================================================================================
        private static bool SearchPenitent(string name, out GameObject found)
        {
            found = null;

            Gameplay.GameControllers.Penitent.Penitent local;
            try
            {
                local = Core.Logic != null ? Core.Logic.Penitent : null;
            }
            catch (Exception)
            {
                local = null;
            }

            if (local == null)
                return false;

            // ⚠️ UNA VOLTA PER SCENA. Il `Penitent` è UNO e i riferimenti ai prefab che porta sono
            // asset: non cambiano mentre si gioca. Scandirlo a ogni nome non risolverebbe niente in
            // più e costerebbe quanto un boss, ogni volta.
            if (_penitentHarvested)
                return false;

            try
            {
                Harvest(local, MAX_PENITENT_COMPONENTS, _cache, SourcePenitent);
                _penitentHarvested = true;
            }
            catch (Exception e)
            {
                Fail("Harvest/Penitent", e);
            }

            return _cache.TryGetValue(name, out found) && found != null;
        }

        private static void BuildCatalog()
        {
            _catalogAt = Time.unscaledTime;
            _catalog.Clear();
            _namesBySource[SourceBoss] = 0; // il catalogo si ricostruisce: il conteggio riparte con lui

            List<Enemy> bosses = FindBosses();
            for (int i = 0; i < bosses.Count; i++)
            {
                try
                {
                    Harvest(bosses[i], MAX_BOSS_COMPONENTS, _catalog, SourceBoss);
                }
                catch (Exception e)
                {
                    Fail("HarvestBoss", e);
                }
            }

            _catalogHasNames = _catalog.Count > 0;

            // Una riga sola per catalogo, e solo quando CAMBIA: serve a leggere nel log "cosa questa
            // macchina sa ricostruire" senza allagare una fight di dieci secondi.
            string signature = Signature();
            if (signature != _catalogLogged)
            {
                _catalogLogged = signature;
                ModLog.Info($"{Marker} {VerbCatalog} scena={_scene} boss={bosses.Count} " +
                            $"nomi={_catalog.Count} [{signature}] (strada 1: i campi della copia " +
                            $"locale del boss)");
            }
        }

        private static string Signature()
        {
            StringBuilder builder = new StringBuilder();
            int shown = 0;

            foreach (string name in _catalog.Keys)
            {
                if (shown >= MAX_CATALOG_NAMES_LOGGED)
                {
                    builder.Append(" +").Append(_catalog.Count - shown).Append(" altri");
                    break;
                }

                if (shown > 0)
                    builder.Append(' ');

                builder.Append(name);
                shown++;
            }

            return builder.ToString();
        }

        /// <summary>
        /// I boss della scena. Il predicato e' **il namespace del tipo**, come nella sonda: un boss e'
        /// un `Enemy` sotto `Gameplay.GameControllers.Bosses`. Non e' un elenco di nomi, quindi non
        /// invecchia con un boss nuovo.
        /// </summary>
        private static List<Enemy> FindBosses()
        {
            List<Enemy> found = new List<Enemy>();

            GameObject[] roots;
            try
            {
                roots = SceneManager.GetActiveScene().GetRootGameObjects();
            }
            catch (Exception)
            {
                return found;
            }

            for (int i = 0; i < roots.Length; i++)
            {
                GameObject root = roots[i];
                if (root == null)
                    continue;

                Enemy[] enemies;
                try
                {
                    // `true` = anche i figli spenti: il boss resta inattivo fino alla sua cutscene, ed
                    // e' proprio lo stato in cui puo' servire risolvere un nome.
                    enemies = root.GetComponentsInChildren<Enemy>(true);
                }
                catch (Exception)
                {
                    continue;
                }

                for (int j = 0; j < enemies.Length; j++)
                {
                    Enemy enemy = enemies[j];
                    if (enemy == null || !IsBossType(enemy))
                        continue;

                    found.Add(enemy);
                }
            }

            return found;
        }

        private static bool IsBossType(Enemy enemy)
        {
            string space = enemy.GetType().Namespace;
            return !string.IsNullOrEmpty(space) &&
                   space.StartsWith("Gameplay.GameControllers.Bosses", StringComparison.Ordinal);
        }

        /// <summary>
        /// I campi di un oggetto: si riflette sui suoi componenti (figli compresi), e si SEGUONO i
        /// riferimenti ai componenti d'attacco (`areaAttack`, `maceImpact`, `jumpAttack`…): è là che
        /// vivono `areaPrefab` e `objectsToInstantiate`, cioè il vocabolario vero dell'attaccante.
        /// Due passi al massimo, per non tirare dentro la scena intera dalle loro referenze.
        ///
        /// ⚠️ QUESTA FUNZIONE ERA `HarvestBoss(Enemy)` E SI CHIAMAVA COSÌ PERCHÉ ANDAVA SOLO SUL BOSS.
        /// Il camminatore era già generico (`boss.gameObject`): allargarlo ai nemici e al `Penitent`
        /// non ha richiesto una riga di riflessione nuova — solo di dire DOVE si scrivono i nomi
        /// trovati (`sink`) e QUANTI componenti si guardano (`maxComponents`).
        /// </summary>
        /// <param name="root">L'oggetto da scandire (boss, nemico comune, `Penitent`).</param>
        /// <param name="maxComponents">Il tetto dei componenti: per il boss 96, per un nemico 64, per il `Penitent` 128.</param>
        /// <param name="sink">Dove si scrivono i nomi trovati: `_catalog` per il boss, `_cache` per le sorgenti larghe.</param>
        /// <param name="source">L'etichetta della sorgente, e serve a una cosa sola: leggere nel log da dove viene un nome.</param>
        private static void Harvest(Component root, int maxComponents, Dictionary<string, GameObject> sink, string source)
        {
            if (root == null || sink == null)
                return;

            Component[] own;
            try
            {
                own = root.gameObject.GetComponentsInChildren<Component>(true);
            }
            catch (Exception)
            {
                return;
            }

            if (own == null)
                return;

            List<CompRef> queue = new List<CompRef>();
            HashSet<Component> visited = new HashSet<Component>();
            HashSet<object> visitedData = new HashSet<object>();

            int count = own.Length < maxComponents ? own.Length : maxComponents;
            for (int i = 0; i < count; i++)
            {
                Component component = own[i];
                if (!IsReflectable(component))
                    continue;

                if (visited.Add(component))
                    queue.Add(new CompRef(component, 0));
            }

            for (int qi = 0; qi < queue.Count && qi < maxComponents; qi++)
            {
                CompRef reference = queue[qi];
                if (reference.Comp == null)
                    continue;

                FieldInfo[] fields = FieldsOf(reference.Comp.GetType());
                if (fields == null)
                    continue;

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
                        Add(value as GameObject, sink, source);
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
                            for (int i = 0; i < list.Count; i++)
                                Add(list[i] as GameObject, sink, source);
                        }
                        else if (IsPlainData(element) && TypeHasGameObject(element, 1))
                        {
                            for (int i = 0; i < list.Count; i++)
                                AddPlainData(list[i], visitedData, 1, sink, source);
                        }

                        continue;
                    }

                    if (IsPlainData(fieldType) && TypeHasGameObject(fieldType, 1))
                    {
                        if (value != null)
                            AddPlainData(value, visitedData, 1, sink, source);

                        continue;
                    }

                    // I riferimenti ai componenti d'attacco: si seguono solo se sono `EnemyAttack`
                    // (il tipo che porta i campi dei prefab) e solo per due passi.
                    Component component = value as Component;
                    if (component != null && component is EnemyAttack && reference.Depth < 2)
                    {
                        if (visited.Add(component))
                            queue.Add(new CompRef(component, reference.Depth + 1));
                    }
                }
            }
        }

        /// <summary>
        /// I campi di un tipo, in cache per tipo. È la differenza fra «una `GetFields` per componente»
        /// e «una `GetFields` per tipo»: con la ricerca larga su tutti i nemici la prima sarebbe
        /// diventata il costo dominante, e i campi di un tipo non cambiano a runtime.
        /// </summary>
        private static FieldInfo[] FieldsOf(Type type)
        {
            if (type == null)
                return null;

            FieldInfo[] cached;
            if (_fieldsOfType.TryGetValue(type, out cached))
                return cached;

            FieldInfo[] fields;
            try
            {
                fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            catch (Exception)
            {
                return null;
            }

            _fieldsOfType[type] = fields;
            return fields;
        }

        private static void AddPlainData(object value, HashSet<object> visited, int depth,
                                         Dictionary<string, GameObject> sink, string source)
        {
            if (value == null || depth > 2)
                return;

            // Guardia sui cicli: una struct che contiene se stessa (o due oggetti che si puntano)
            // farebbe ricorrere la riflessione fino allo stack overflow, che in Unity e' un crash.
            if (!value.GetType().IsValueType && !visited.Add(value))
                return;

            FieldInfo[] fields = FieldsOf(value.GetType());
            if (fields == null)
                return;

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

                    Add(raw as GameObject, sink, source);
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

                AddPlainData(inner, visited, depth + 1, sink, source);
            }
        }

        /// <summary>
        /// Un nome nuovo nella destinazione. La chiave e' il nome del PREFAB (ripulito dai suffissi
        /// d'istanza), che e' esattamente cio' che viaggia nel pacchetto: il peer manda un nome, non
        /// un handle, perche' gli handle non sono gli stessi su due macchine.
        ///
        /// ⚠️ «VINCE LA PRIMA»: se il nome c'e' gia', non si sovrascrive. Vale fra sorgenti diverse
        /// (boss > nemici > penitente) e vale dentro la stessa: il primo prefab con quel nome e'
        /// quello che si usa, e il marker lo dichiara con `da=`.
        /// </summary>
        private static void Add(GameObject go, Dictionary<string, GameObject> sink, string source)
        {
            if (go == null || sink == null)
                return;

            string name = Clean(go.name);
            if (string.IsNullOrEmpty(name))
                return;

            if (sink.ContainsKey(name))
                return;

            sink[name] = go;

            // Il conteggio per sorgente: `_catalog` si svuota a ogni ricostruzione, quindi il numero
            // del boss viene azzerato in `BuildCatalog` e questo contatore resta la misura vera di
            // «quanti nomi ha portato QUESTA sorgente».
            if (source == null)
                return;

            int seen;
            _namesBySource.TryGetValue(source, out seen);
            _namesBySource[source] = seen + 1;
        }

        // =============================================================================================
        //  IL DEDUP — la chiave e la finestra.
        // =============================================================================================

        /// <summary>
        /// ⭐ LA CHIAVE: `prefab + posizione ARROTONDATA`, con la convenzione che il progetto usa gia'
        /// nei log (`x:F0` / `y:F0`). Arrotondare e' cio' che rende la chiave **identica su due
        /// macchine** che misurano lo stesso evento: la posizione vera puo' differire di millesimi per
        /// il float, un intero no.
        ///
        /// ⚠️ DICHIARATO: con `F0` due aree **diverse** a meno di un'unita' l'una dall'altra, dentro la
        /// finestra, si fondono in una. Per la summon di ElderBrother non capita (`distanceBetweenAreas`
        /// = 1,5 unita': misurato −823,16 → −824,66 → −826,16), ma per un boss che spara raffiche
        /// ravvicinate puo' capitare: e' il caso "effetti persi" del referto.
        /// </summary>
        private static string Key(string name, Vector3 position)
        {
            return name + "|" + Round(position.x) + "|" + Round(position.y);
        }

        /// <summary>
        /// L'arrotondamento della chiave. Il `-0` e' il motivo per cui questo metodo esiste: `(-0.4)`
        /// formattato con `F0` da `-0`, e `-0` non e' `0` in una stringa. Due macchine d'accordo sulla
        /// posizione devono dare la stessa chiave.
        /// </summary>
        private static string Round(float value)
        {
            double rounded = Math.Round((double)value, 0, MidpointRounding.AwayFromZero);
            if (rounded == 0d)
                rounded = 0d;

            return rounded.ToString("F0", CultureInfo.InvariantCulture);
        }

        /// <summary>Registra che QUESTA macchina ha istanziato quel prefab in quel posto, adesso.</summary>
        private static void Note(string name, Vector3 position)
        {
            if (_seen.Count >= SEEN_SWEEP_AT)
                Sweep();

            _seen[Key(name, position)] = Time.unscaledTime;
        }

        /// <summary>
        /// Ripulisce le voci scadute: la tabella non deve crescere per una sessione intera. Non e' una
        /// ottimizzazione, e' un tetto di memoria in una stanza in cui si fighta per minuti.
        /// </summary>
        private static void Sweep()
        {
            float now = Time.unscaledTime;
            List<string> dead = new List<string>();

            foreach (KeyValuePair<string, float> pair in _seen)
            {
                if (now - pair.Value > DEDUP_SECONDS * 2f)
                    dead.Add(pair.Key);
            }

            for (int i = 0; i < dead.Count; i++)
                _seen.Remove(dead[i]);
        }

        // =============================================================================================
        //  IL FILTRO DEL MITTENTE (strutturale, in cache) e le letture.
        // =============================================================================================

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
                value = FirstWeapon(prefab) != null || FirstProjectile(prefab) != null;
            }
            catch (Exception)
            {
                value = false;
            }

            _interesting[id] = value;
            return value;
        }

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

        private static bool OtherPlayerHere()
        {
            try
            {
                return EnemySyncManager.AnyOtherPlayerInMyScene();
            }
            catch (Exception)
            {
                return false;
            }
        }

        // =============================================================================================
        //  RIFLESSIONE: tipi e campi. In cache per tipo, cosi' il costo e' una volta per tipo.
        // =============================================================================================

        private static bool IsReflectable(Component component)
        {
            if (component == null)
                return false;

            string name = component.GetType().Name;
            return name != "Transform" && name != "RectTransform";
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
        /// Un tipo "dati semplici": una struct o una classe serializzabile che NON e' un oggetto
        /// Unity. E' la forma di `DashAttackInstantiations`/`objectsToInstantiate`, cioe' la lista in
        /// cui il prefab sta DENTRO una struct (`prefabToInstantiate`) e non in un campo diretto.
        /// </summary>
        private static bool IsPlainData(Type type)
        {
            if (type == null || type == typeof(string) || type == typeof(GameObject))
                return false;

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
                return false;

            return type.IsValueType || (type.IsClass && type.IsSerializable);
        }

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
                FieldInfo[] fields = type.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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

        // =============================================================================================
        //  TESTO E LOG
        // =============================================================================================

        /// <summary>
        /// Toglie i suffissi d'ISTANZA, e solo quelli: `(Clone)` di Unity e il contatore ` (12)`.
        /// NON toglie le cifre finali di un nome vero (`LandingImpact3`), perche' qui il nome e' quello
        /// del PREFAB e le cifre possono farne parte.
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

        /// <summary>
        /// Una posizione in una riga di log, con la virgola decimale INGLESE. Non e' pedanteria: su una
        /// macchina in italiano `F1` scrive `12,3`, e due coordinate separate da virgola diventano una
        /// lista di numeri senza struttura — illeggibile proprio a chi confronta i due log.
        /// </summary>
        private static string Pos(Vector3 position)
        {
            return "(" + F(position.x) + ", " + F(position.y) + ")";
        }

        private static string F(float value)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// I millisecondi della risoluzione, con la virgola decimale INGLESE e tre decimali: una
        /// ricerca di riflessione su un oggetto sta fra 0,05 e 3 ms, quindi con un solo decimale
        /// (o con la virgola italiana) non si leggerebbe.
        /// </summary>
        private static string F3(double value)
        {
            return value.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static void LogThrottled(ref float last, string line)
        {
            float now = Time.unscaledTime;
            if (now - last < LOG_INTERVAL)
                return;

            last = now;
            ModLog.Info(line);
        }

        private static void SkipThrottled(string why)
        {
            LogThrottled(ref _lastSkipLog, $"{Marker} {VerbSkip}: {why}");
        }

        /// <summary>
        /// Il riepilogo di fine scena: i conteggi di prima **più la misura della risoluzione** — chi ha
        /// vinto, quanti nomi ha portato ciascuna sorgente, quante volte la ricerca larga è girata e
        /// qual è stata la peggiore (ms). È il numero che va nel referto, e non si può stimare.
        /// </summary>
        private static void PrintSummary()
        {
            double average = _resolves > 0 ? _resolveTotalMs / _resolves : 0d;

            ModLog.Info($"{Marker} riepilogo scena={_scene} mandati={_sends} istanziati={_instantiated} " +
                        $"scartatiPerDedup={_deduped} nonRisolti={_unresolvedCount} frenati={_braked} " +
                        $"catalogo={_catalog.Count} risolti=[{Counts(_bySource)}] " +
                        $"nomi=[{Counts(_namesBySource)}] risoluzioni={_resolves} " +
                        $"max={F3(_resolveMaxMs)}ms medio={F3(average)}ms " +
                        $"ricercheLarghe={_wideSearches} saltateDalTetto={_wideSkipped} " +
                        $"nemiciScanditi={_enemiesScanned} nomiNonRisolti={_unresolved.Count}");
        }

        /// <summary>I quattro numeri di una tabella per sorgente, sempre tutti e quattro, anche a zero.</summary>
        private static string Counts(Dictionary<string, int> table)
        {
            return SourceBoss + ":" + Count(table, SourceBoss) + " " +
                   SourceEnemies + ":" + Count(table, SourceEnemies) + " " +
                   SourcePenitent + ":" + Count(table, SourcePenitent) + " " +
                   SourceCache + ":" + Count(table, SourceCache);
        }

        private static int Count(Dictionary<string, int> table, string key)
        {
            int value;
            return table.TryGetValue(key, out value) ? value : 0;
        }

        /// <summary>
        /// L'ultima riga di difesa, e l'unico `catch` vuoto del file, di proposito: se fallisse anche
        /// `ModLog.Error`, l'eccezione uscirebbe dal punto d'ingresso e arriverebbe DENTRO il gioco.
        /// </summary>
        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error($"{Marker} relay fallito in {where} (nessun effetto sul gioco): " +
                             e.GetType().Name + ": " + e.Message);
            }
            catch
            {
            }
        }

        /// <summary>Un componente del boss da riflettere, con la profondita' a cui e' stato raggiunto.</summary>
        private sealed class CompRef
        {
            public readonly Component Comp;
            public readonly int Depth;

            public CompRef(Component comp, int depth)
            {
                Comp = comp;
                Depth = depth;
            }
        }
    }
}
