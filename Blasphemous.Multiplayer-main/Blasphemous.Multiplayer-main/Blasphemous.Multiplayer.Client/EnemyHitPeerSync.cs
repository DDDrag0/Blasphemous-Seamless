using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    // =====================================================================================================
    // ⭐⭐ IL VERSO NUOVO: IL NEMICO CHE FERISCE IL PEER — il passo ② del danno del boss.
    //
    // LA DIAGNOSI (`scratch\DIAGNOSI-DANNO-BOSS-COOP.md`) aveva una sola conclusione: **il canale
    // nemico→giocatore non esiste**, e non e' un guasto di un punto — e' un pezzo di protocollo mai
    // costruito. Ogni macchina ferisce il PROPRIO `Penitent` con la pipeline del gioco, che e' a
    // collider; il padrone della simulazione ha la copia del nemico che mena, ma l'altro giocatore,
    // su quella macchina, e' solo il proxy d'aggio (`_coop_remote_target`), che **non era
    // danneggiabile**. ⇒ il colpo del boss mirato al peer arrivava su un collider che non accettava
    // danno: zero vita, e (prima di `c89c368`) anche una `NullReferenceException`.
    //
    // LA FORMA DEL FIX, in una frase: **il proxy diventa un `IDamageable` che non tocca mai il
    // `Penitent` locale e INOLTRA il colpo al peer**, il quale lo applica al **proprio** `Penitent`
    // con la porta d'ingresso del gioco (`Penitent.Damage(Hit)`), esattamente come fa il gioco quando
    // il colpo arriva sulla sua macchina.
    //
    // ⭐ E' GENERICO PER COSTRUZIONE, e questa e' la proprieta' che conta. Il proxy e' UNO SOLO nel mod
    // (`_coop_remote_target`, `DontDestroyOnLoad`, layer `Penitent`, riposizionato da
    // `EnemySyncManager.ApplyAggroTarget` su DOVE STA IL PEER): l'inoltro avviene a livello di
    // **collisione**, cioe' dove il gioco ha gia' deciso che un colpo e' atterrato su quel posto. Non
    // c'e' nessun `IsBoss`, nessun `ElderBrother`, nessun elenco di boss: **qualunque nemico e
    // qualunque boss** che mena il proxy inoltra il suo colpo, senza che questo file sappia quale sia.
    // Il boss della fight misurata (`ElderBrother`) e' solo il primo che arriva.
    //
    // ⚠️ E IL FILTRO CHE RENDE LA COSA SICURA E' UNO: **l'attaccante deve risolversi a un `Enemy`**
    // (`hit.AttackingEntity.GetComponentInParent&lt;Enemy&gt;()`). Non e' un dettaglio: le trappole, le
    // spine, gli `AreaEffect` e i `SimpleDamageArea` di livello costruiscono l'`Hit` con
    // `AttackingEntity` = loro stessi (o un `AreaAttackDummyEntity`), e quei pericoli **esistono su
    // entrambe le macchine**: il peer li prende gia' per conto suo, localmente. Inoltrarli
    // significherebbe **danno doppio** — la trappola ferisce il suo `Penitent` e il nostro inoltro lo
    // ferisce di nuovo. Idem per un attacco del giocatore locale (`AttackingEntity` = il `Penitent`).
    //
    // ⚠️ E L'ALTRA META' DELLA SICUREZZA: il colpo **non viene mai applicato due volte sulla stessa
    // macchina**. Il proxy non chiama mai `Core.Logic.Penitent` (non lo tocca: la sua `Damage` fa solo
    // l'inoltro); il ricevente lo applica una volta sola con la pipeline del gioco, che ha gia' i suoi
    // cancelli — `CanTakeHit` (`Status.Invulnerable`, `PrayerProtectionEnabled`, `IsHurt`) e
    // `DeltaRecoverTime &lt; RecoverTime` (`PenitentDamageArea.TakeDamage`): due pacchetti ravvicinati
    // valgono UN colpo, come due colpi ravvicinati del gioco. Il server, infine, **esclude il mittente**
    // (`playerIp != ip`) quindi la macchina che ha inoltrato non riceve mai il proprio pacchetto: non
    // esiste nessun anello di ritorno.
    //
    // ⚠️ COSA NON SI INOLTRA, DICHIARATO:
    //   1. **il contatto** (`ContactDamage`): resta SALTATO da `c89c368`
    //      (`ContactDamageProxyIgnore_Patch`). Riaprirlo significherebbe riaprire anche il difetto che
    //      quel commit ha chiuso: il metodo alza `IsTargetOverlapped` PRIMA di danneggiare, e gli
    //      attacchi che leggono quella proprieta' colpiscono `Core.Logic.Penitent` senza misurare la
    //      distanza (il giocatore locale, a venti unita', prendeva il colpo di un nemico che aveva
    //      "sentito" solo il proxy). ⇒ i nemici con `DamageByContact` NON feriscono il peer.
    //   2. **i colpi il cui attaccante e' un proiettile/area nata a se'** (non un `Enemy`): vedi sopra,
    //      e' il filtro che evita il danno doppio.
    //   3. **i tipi di danno che il `Penitent` non gestisce** (`Stunt`, `OptionalStunt`): la sua
    //      `PenitentDamageArea.TakeDamage` finisce in `throw new ArgumentOutOfRangeException()`
    //      (`Assembly-CSharp.cs:145500`). Il gioco non li usa contro il giocatore; il filtro e'
    //      difensivo, cosi' un pacchetto storto non puo' far esplodere il ricevente.
    // =====================================================================================================

    /// <summary>
    /// Il proxy d'aggio del mod, reso **danneggiabile**: e' l'unica cosa che il gioco vede al posto
    /// dell'altro giocatore, quindi e' l'unico punto in cui il colpo di un nemico puo' essere
    /// intercettato sulla macchina che lo simula.
    ///
    /// ⚠️ NON tocca MAI il `Penitent` locale. Non lo legge, non lo danneggia, non lo sposta: chiama
    /// solo l'inoltro. E' la lezione di `AggroProxyPatches` — il proxy e' un fantasma, e un fantasma
    /// non deve poter ferire chi lo guarda.
    ///
    /// Gli altri tre membri di <see cref="IDamageable"/> sono obbligatori ma inutili qui: il proxy non
    /// sanguina e non fa scintille (non ha né sangue né sprite), e la sua posizione e' quella del
    /// giocatore remoto. `SparkOnImpact`/`BleedOnImpact` a `false` sono anche la risposta giusta per
    /// il chiamante: sono i due predicati che il gioco consulta PRIMA di danneggiare, e il proxy non
    /// deve produrre effetti propri.
    /// </summary>
    public class CoopRemoteTargetDamageable : MonoBehaviour, IDamageable
    {
        /// <summary>
        /// Il colpo e' atterrato sul proxy: non si applica niente qui, si inoltra al peer.
        /// Vedi <see cref="EnemyHitPeer.Forward"/> per i cancelli e per il perche'.
        /// </summary>
        public void Damage(Hit hit)
        {
            EnemyHitPeer.Forward(hit);
        }

        /// <summary>La posizione del proxy, cioe' dove questa macchina crede che stia l'altro giocatore.</summary>
        public Vector3 GetPosition()
        {
            return transform.position;
        }

        /// <summary>Mai: il proxy non deve chiedere scintille per un colpo che non e' suo (nessun danno locale).</summary>
        public bool SparkOnImpact()
        {
            return false;
        }

        /// <summary>Mai: il proxy non sanguina e non deve produrre sangue sulla macchina sbagliata.</summary>
        public bool BleedOnImpact()
        {
            return false;
        }
    }

    /// <summary>
    /// Il canale nemico→giocatore: due metodi pubblici e una forma di pacchetto.
    ///
    /// PERCHE' NON UN `NetworkType` NUOVO — e perche' questa e' la scelta meno invasiva.
    /// Il mod ha gia' un canale che fa esattamente il viaggio che serve: `NetworkType.Attack`, che il
    /// server inoltra **a tutti i peer della stessa scena tranne il mittente**, con il nome del
    /// mittente PREMESSO (`Server.cs:676-699`). Il verso giocatore→nemico (`[Danno] relay`,
    /// `EnemyDamageRequest`) non puo' essere riusato: il suo payload e' un HANDLE DI NEMICO, e la sua
    /// applicazione e' `((IDamageable)enemy).Damage(hit)` — cioe' ferisce **il nemico**, l'opposto di
    /// quello che serve. ⇒ si riusa il canale Attack, distinguendo il nuovo verso con un valore di
    /// `AttackType` (`EnemyHit = 200`), che e' un byte libero dell'enum.
    ///
    /// ⚠️ VANTAGGIO MISURABILE: **zero righe di protocollo e zero righe di server**. Nessun
    /// `NetworkType` nuovo, nessuna `PROTOCOL_VERSION` da alzare (resta 11), nessuna patch al
    /// `blas1mpserver.dll`. E le due macchine possono restare disallineate senza danni: un peer
    /// vecchio legge il tipo 200, non lo trova in `attacks.json` e **esce** (`AttackManager.cs:31`)
    /// senza eccezioni; un server vecchio inoltra i byte senza interpretarli. Il caso peggiore di un
    /// disallineamento e' **il comportamento di oggi**: nessun danno al peer. Mai un crash.
    ///
    /// LA FORMA DEL PACCHETTO (dopo il nome del mittente premesso dal server; `startIdx` = indice del
    /// primo byte del payload vero):
    /// <code>
    ///   +0   byte    AttackType.EnemyHit (200)
    ///   +1   float   DamageAmount
    ///   +5   byte    DamageType
    ///   +6   byte    DamageElement
    ///   +7   ushort  bandiere (9 booleani dell'Hit, vedi FlagsFrom)
    ///   +9   float   Force
    ///   +13  uint    handle del nemico (per ritrovare la copia locale dell'attaccante)
    ///   +17  byte    lunghezza del nome del DESTINATARIO
    ///   +18  string  nome del destinatario (UTF8)
    ///   ..   byte    lunghezza dell'id del suono
    ///   ..   string  id del suono d'impatto (UTF8)
    /// </code>
    /// Il nome del destinatario c'e' perche' il canale Attack e' un broadcast di scena: senza, il
    /// danno andrebbe a chiunque sia nella stanza. Il suono viaggia come **stringa esatta** e non come
    /// chiave reinterpretata: e' il percorso di banca che il nemico usa davvero (`Hit.HitSoundId`,
    /// campo `[EventRef]` del prefab), e il ricevente lo suona sulla propria copia del gioco.
    /// </summary>
    internal static class EnemyHitPeer
    {
        /// <summary>
        /// Il prefisso di TUTTE le righe di questo verso. Due sole righe contano, e sono quelle
        /// richieste dal mandato: **una quando il colpo viene INOLTRATO** (sulla macchina che simula il
        /// nemico) e **una quando viene APPLICATO** sul peer. Le righe `non inoltrato` / `non
        /// applicato` esistono per rendere spiegabile il caso in cui il danno non arriva: senza,
        /// "non e' successo niente" e "il pacchetto e' stato scartato per un motivo preciso" sarebbero
        /// indistinguibili nel log, che e' la lezione di `[Danno] relay`.
        /// </summary>
        internal const string Marker = "[DannoPeer]";

        /// <summary>
        /// Distanza minima fra due inoltri dello STESSO nemico. Non e' un cancello sul danno — quello
        /// lo mette il ricevente con le sue cornici di invulnerabilita' (`RecoverTime = 0.5f`) — e'
        /// un cancello sul TRAFFICO: il proxy viene riposizionato sulla posizione remota a ogni giro di
        /// scansione (`RESCAN_INTERVAL`), quindi puo' entrare e uscire dalla stessa area d'attacco piu'
        /// volte, e ogni ingresso e' un `OnTriggerEnter2D` nuovo.
        /// </summary>
        private const float MIN_FORWARD_INTERVAL = 0.2f;

        /// <summary>Quando ogni nemico ha inoltrato l'ultimo colpo (chiave: instance id del nemico).</summary>
        private static readonly Dictionary<int, float> _lastForward = new Dictionary<int, float>();

        /// <summary>
        /// Quando e' stata scritta l'ultima riga `non inoltrato` / `non applicato`.
        ///
        /// Serve un freno perche' `SkipForward` viene chiamata anche per i colpi che NON ci riguardano:
        /// una trappola o un'area di livello addosso al proxy puo' entrare e uscire di continuo, e una
        /// riga per ingresso renderebbe il log illeggibile proprio dove servirebbe leggerlo. Le due
        /// righe che CONTANO (inoltrato / applicato) non sono filtrate: una per colpo, sempre.
        /// </summary>
        private static float _lastSkipLog;

        private const float SKIP_LOG_INTERVAL = 1f;

        // ---------------------------------------------------------------------------------------------
        // LATO SENDER: la macchina che simula il nemico.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Chiamata dalla `Damage` del proxy. Decide se il colpo si puo' inoltrare, e lo fa.
        /// Non lancia mai: un'inoltro mancato non deve fermare l'attacco del gioco (che e' gia' stato
        /// deviato qui: il colpo non ha altre destinazioni su questa macchina).
        /// </summary>
        internal static void Forward(Hit hit)
        {
            try
            {
                if (!Main.Multiplayer.NetworkManager.IsConnected)
                {
                    SkipForward("non connesso");
                    return;
                }

                Enemy attacker = AttackerOf(hit);
                if (attacker == null)
                {
                    SkipForward("l'attaccante non e' un NEMICO (trappola, area di livello o giocatore): " +
                                "il peer lo subisce per conto suo, inoltrarlo lo ferirebbe due volte");
                    return;
                }

                if (!IsDamageTypeThePenitentHandles(hit.DamageType))
                {
                    SkipForward("tipo di danno " + hit.DamageType + " non gestito dal Penitent");
                    return;
                }

                float amount = hit.DamageAmount;
                if (!(amount > 0f) || float.IsNaN(amount) || float.IsInfinity(amount))
                {
                    SkipForward("danno non positivo (" + amount.ToString("F2") + ")");
                    return;
                }

                string peerName;
                if (!EnemySyncManager.TryGetRemotePlayerHereAndAlive(out peerName))
                {
                    SkipForward("nessun altro giocatore QUI e VIVO");
                    return;
                }

                int key = attacker.GetInstanceID();
                float last;
                if (_lastForward.TryGetValue(key, out last) && Time.time - last < MIN_FORWARD_INTERVAL)
                {
                    SkipForward("lo stesso nemico ha gia' inoltrato un colpo " +
                                (Time.time - last).ToString("F2") + " s fa (il proxy rientra nell'area)");
                    return;
                }

                if (_lastForward.Count > 64)
                    _lastForward.Clear();

                _lastForward[key] = Time.time;

                uint handle;
                if (!EnemySyncManager.TryGetEnemyHandle(attacker, out handle))
                    handle = 0;

                string enemyId;
                if (!EnemySyncManager.TryGetEnemyId(attacker, out enemyId))
                    enemyId = null;

                byte[] payload = BuildPayload(peerName, handle, hit);

                Main.Multiplayer.NetworkManager.SendAttackPayload(payload);

                ModLog.Info($"{Marker} colpo del NEMICO inoltrato al peer: " +
                            $"nemico={(enemyId ?? "handle=" + handle.ToString("X8"))} " +
                            $"danno={amount:F0} tipo={hit.DamageType} elem={hit.DamageElement} " +
                            $"bandiere={FlagsFrom(hit):X4} a={peerName} " +
                            $"nemicoPos={Pos(attacker.transform.position)} " +
                            $"proxyPos={ProxyPosition()}");
            }
            catch (Exception e)
            {
                ModLog.Error($"{Marker} inoltro fallito ({e.GetType().Name}): {e.Message} - " +
                             "il colpo non e' stato applicato ne' qui ne' la'");
            }
        }

        /// <summary>
        /// L'unico filtro che rende il verso sicuro: il colpo deve venire da un `Enemy`.
        ///
        /// `GetComponentInParent` e non `GetComponent`, perché molti attacchi dei nemici costruiscono
        /// l'`Hit` con `AttackingEntity = base.EntityOwner.gameObject` (il nemico, `:74086`, `:75367`)
        /// mentre altri lo costruiscono con un componente FIGLIO (`AttackingEntity = base.gameObject`,
        /// `:75529`): risalire i parent copre entrambe le forme. Un `DamageObject` di livello, un
        /// `SimpleDamageArea`, una trappola o il `Penitent` locale non hanno un `Enemy` fra i parent:
        /// si fermano qui, e nessuno di quei pericoli (che ogni macchina applica per conto suo) viene
        /// contato due volte.
        /// </summary>
        private static Enemy AttackerOf(Hit hit)
        {
            if (hit.AttackingEntity == null)
                return null;

            return hit.AttackingEntity.GetComponentInParent<Enemy>();
        }

        /// <summary>
        /// I quattro tipi che `PenitentDamageArea.TakeDamage` sa trattare (`Assembly-CSharp.cs:145500`:
        /// `Normal`, `Heavy`, `Simple`, `Critical`). `Stunt` e `OptionalStunt` finiscono nel ramo
        /// `default: throw new ArgumentOutOfRangeException()`, e un pacchetto storto non deve poter
        /// far esplodere il ricevente.
        /// </summary>
        private static bool IsDamageTypeThePenitentHandles(DamageArea.DamageType type)
        {
            return type == DamageArea.DamageType.Normal
                || type == DamageArea.DamageType.Heavy
                || type == DamageArea.DamageType.Simple
                || type == DamageArea.DamageType.Critical;
        }

        private static string ProxyPosition()
        {
            try
            {
                return EnemySyncManager.RemoteTargetProxyPosition();
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// Una posizione in una riga di log, con la virgola decimale INGLESE.
        ///
        /// Non e' pedanteria: su una macchina in italiano `F1` scrive `12,3`, e due coordinate
        /// separate da virgola diventano una lista di quattro numeri senza struttura. Il log deve
        /// restare leggibile a chi lo confronta fra le due macchine.
        /// </summary>
        private static string Pos(Vector3 position)
        {
            return "(" + position.x.ToString("F1", CultureInfo.InvariantCulture) + ", " +
                         position.y.ToString("F1", CultureInfo.InvariantCulture) + ")";
        }

        // ---------------------------------------------------------------------------------------------
        // IL PACCHETTO
        // ---------------------------------------------------------------------------------------------

        /// <summary>Lunghezza della testa fissa: tipo(1) danno(4) tipoDanno(1) elemento(1) bandiere(2) forza(4) handle(4) lenNome(1).</summary>
        private const int HEAD_LENGTH = 18;

        /// <summary>
        /// I booleani dell'`Hit` che cambiano l'esito sul ricevente.
        ///
        /// Sono nove e viaggiano tutti, non solo i tre del canale nemico: qui il destinatario e' un
        /// `Penitent`, e la sua `Penitent.Damage` RAMIFICA su questi (`forceGuardslide` decide il
        /// guardslide invece del danno, `DamageType` sceglie l'animazione di dolore,
        /// `ThrowbackDirByOwnerPosition` decide da dove arriva lo spintone). Un flag perso non sarebbe
        /// una differenza cosmetica: sarebbe un colpo che reagisce in un modo che il gioco non prevede.
        /// </summary>
        private static ushort FlagsFrom(Hit hit)
        {
            ushort flags = 0;

            if (hit.Unnavoidable) flags |= 1;
            if (hit.ForceGuardSlideDirection) flags |= 2;
            if (hit.Unparriable) flags |= 4;
            if (hit.Unblockable) flags |= 8;
            if (hit.DestroysProjectiles) flags |= 16;
            if (hit.DontSpawnBlood) flags |= 32;
            if (hit.forceGuardslide) flags |= 64;
            if (hit.CheckOrientationsForGuardslide) flags |= 128;
            if (hit.ThrowbackDirByOwnerPosition) flags |= 256;

            return flags;
        }

        private static void ApplyFlags(Hit hit, ushort flags)
        {
            hit.Unnavoidable = (flags & 1) != 0;
            hit.ForceGuardSlideDirection = (flags & 2) != 0;
            hit.Unparriable = (flags & 4) != 0;
            hit.Unblockable = (flags & 8) != 0;
            hit.DestroysProjectiles = (flags & 16) != 0;
            hit.DontSpawnBlood = (flags & 32) != 0;
            hit.forceGuardslide = (flags & 64) != 0;
            hit.CheckOrientationsForGuardslide = (flags & 128) != 0;
            hit.ThrowbackDirByOwnerPosition = (flags & 256) != 0;
        }

        /// <summary>
        /// L'id del suono d'impatto, **solo se e' un percorso di banca**.
        ///
        /// E' la lezione piu' cara di questo progetto: `Entity.Damage(float, string)` chiama
        /// `Core.Audio.EventOneShotPanned(impactAudioId, ...)` PRIMA di sottrarre la vita, e con una
        /// chiave che non e' un percorso di banca FMOD lancia — il colpo diventa zero danno e
        /// l'eccezione esce dal percorso di danno (`EnemySyncManager.ApplyDamage`, il commento sulla
        /// stringa sbagliata). Il campo del prefab e' un `[EventRef]`, quindi nella pratica e' sempre
        /// `event:/...`; se non lo fosse, meglio **nessun suono** che nessun danno.
        /// </summary>
        private static string SoundFor(Hit hit)
        {
            string id = hit.HitSoundId;
            if (string.IsNullOrEmpty(id) || id.Length > 200)
                return string.Empty;

            return id.StartsWith("event:", StringComparison.Ordinal) ? id : string.Empty;
        }

        private static byte[] BuildPayload(string targetPlayer, uint handle, Hit hit)
        {
            string sound = SoundFor(hit);

            byte[] nameBytes = Encoding.UTF8.GetBytes(targetPlayer ?? string.Empty);
            if (nameBytes.Length > 255)
                Array.Resize(ref nameBytes, 255);

            byte[] soundBytes = Encoding.UTF8.GetBytes(sound);
            if (soundBytes.Length > 255)
                Array.Resize(ref soundBytes, 255);

            List<byte> bytes = new List<byte>(HEAD_LENGTH + nameBytes.Length + soundBytes.Length);

            bytes.Add((byte)AttackType.EnemyHit);
            bytes.AddRange(BitConverter.GetBytes(hit.DamageAmount));
            bytes.Add((byte)hit.DamageType);
            bytes.Add((byte)hit.DamageElement);
            bytes.AddRange(BitConverter.GetBytes(FlagsFrom(hit)));
            bytes.AddRange(BitConverter.GetBytes(hit.Force));
            bytes.AddRange(BitConverter.GetBytes(handle));

            bytes.Add((byte)nameBytes.Length);
            bytes.AddRange(nameBytes);

            bytes.Add((byte)soundBytes.Length);
            bytes.AddRange(soundBytes);

            return bytes.ToArray();
        }

        // ---------------------------------------------------------------------------------------------
        // LATO RICEVENTE: il peer, che applica il colpo al PROPRIO Penitent.
        // ---------------------------------------------------------------------------------------------

        /// <summary>
        /// Applica un colpo arrivato dal canale Attack. `startIdx` e' l'indice del byte
        /// `AttackType.EnemyHit` dentro `data` (cioe' subito dopo il nome del mittente premesso dal
        /// server).
        ///
        /// PERCHE' `Penitent.Damage(Hit)` E NON `DamageArea.TakeDamage`: e' la stessa porta da cui
        /// entra il colpo del gioco. Contiene i cancelli veri — `LungeAttack.Casting` + elemento
        /// `Contact`, `IsOnExecution`, `IsPickingCollectibleItem`, `GuardSlide.Casting`, e la PARATA
        /// (`Parry.IsOnParryChance` -> `Parry.CheckParry`): se il peer sta parando, il colpo viene
        /// parato **da lui**, con la sua parata. Passare dalla hurtbox scavalcherebbe tutto questo.
        /// </summary>
        internal static void ApplyFromWire(string senderName, byte[] data, int startIdx)
        {
            try
            {
                if (data == null || startIdx < 0 || data.Length < startIdx + HEAD_LENGTH)
                {
                    SkipApply("pacchetto troppo corto (" +
                              (data == null ? "null" : data.Length.ToString()) + " byte)");
                    return;
                }

                float amount = BitConverter.ToSingle(data, startIdx + 1);
                byte damageType = data[startIdx + 5];
                byte damageElement = data[startIdx + 6];
                ushort flags = BitConverter.ToUInt16(data, startIdx + 7);
                float force = BitConverter.ToSingle(data, startIdx + 9);
                uint handle = BitConverter.ToUInt32(data, startIdx + 13);
                int index = startIdx + 17;

                int nameLength = data[index];
                index++;
                if (data.Length < index + nameLength + 1)
                {
                    SkipApply("nome del destinatario troncato");
                    return;
                }

                string targetName = Encoding.UTF8.GetString(data, index, nameLength);
                index += nameLength;

                int soundLength = data[index];
                index++;
                if (data.Length < index + soundLength)
                {
                    SkipApply("id del suono troncato");
                    return;
                }

                string soundId = Encoding.UTF8.GetString(data, index, soundLength);

                // 1. E' PER ME? Il canale Attack e' un broadcast di scena: senza questo controllo il
                //    colpo andrebbe a chiunque sia nella stanza.
                string me = Main.Multiplayer.PlayerName;
                if (string.IsNullOrEmpty(me) || targetName != me)
                {
                    SkipApply("non e' per me (destinatario=" +
                              (string.IsNullOrEmpty(targetName) ? "(vuoto)" : targetName) + ", io=" +
                              (string.IsNullOrEmpty(me) ? "(senza nome)" : me) + ")");
                    return;
                }

                // 2. C'E' QUALCUNO DA FERIRE? Un colpo sul morto non e' danno: e' la morte che si
                //    confonde con un colpo (e la morte ha gia' il suo canale).
                Penitent penitent = Core.Logic.Penitent;
                if (penitent == null)
                {
                    SkipApply("nessun Penitent locale (menu, cambio di livello)");
                    return;
                }

                if (PlayerDeathSync.IsLocalPlayerDead())
                {
                    SkipApply("il mio Penitent e' gia' morto");
                    return;
                }

                // 3. IL PACCHETTO E' RAGIONEVOLE?
                if (!(amount > 0f) || float.IsNaN(amount) || float.IsInfinity(amount))
                {
                    SkipApply("danno non valido (" + amount.ToString("F2") + ")");
                    return;
                }

                DamageArea.DamageType type = (DamageArea.DamageType)damageType;
                if (!IsDamageTypeThePenitentHandles(type))
                {
                    SkipApply("tipo di danno non gestito (" + type + ")");
                    return;
                }

                // 4. L'ATTACCANTE deve essere un GameObject VERO, e non e' un dettaglio:
                //    `PenitentDamageArea.TakeDamage` fa `hit.AttackingEntity.transform.position` per
                //    orientare l'animazione di dolore (`Assembly-CSharp.cs:145493`) e andrebbe in
                //    NullReferenceException con l'attaccante nullo.
                //    La copia locale del nemico e' la scelta giusta (posizione → direzione dello
                //    spintone, e metrica di morte col nome giusto); se non c'e' — nemico gia' sparito
                //    da questa scena — si ripiega sul proprio `Penitent`, che non e' mai nullo qui, e
                //    il colpo resta un colpo vero (nessuna eccezione, orientamento neutro).
                Enemy attacker = EnemySyncManager.EnemyForHandle(handle);
                GameObject attackerObject = attacker != null ? attacker.gameObject : penitent.gameObject;

                Hit hit = new Hit
                {
                    AttackingEntity = attackerObject,
                    DamageAmount = amount,
                    DamageType = type,
                    DamageElement = (DamageArea.DamageElement)damageElement,
                    Force = force,
                    HitSoundId = soundId
                };

                ApplyFlags(hit, flags);

                float before = penitent.Stats.Life.Current;

                penitent.Damage(hit);

                float after = penitent.Stats.Life.Current;

                // ⭐ LA RIGA CHE PROVA L'APPLICAZIONE. "APPLICATO" e non "ricevuto": dice il fatto
                // misurabile (la vita e' scesa), non l'intenzione.
                ModLog.Info($"{Marker} APPLICATO sul Penitent locale: da={senderName} " +
                            $"nemico={(attacker != null ? attacker.name : "handle=" + handle.ToString("X8"))} " +
                            $"danno={amount:F0} tipo={type} elem={(DamageArea.DamageElement)damageElement} " +
                            $"vitaPrima={before:F0} vitaDopo={after:F0} " +
                            $"perso={Math.Max(0f, before - after):F0}");
            }
            catch (Exception e)
            {
                ModLog.Error($"{Marker} applicazione fallita ({e.GetType().Name}): {e.Message}");
            }
        }

        private static void SkipForward(string why)
        {
            LogSkip("non inoltrato", why);
        }

        private static void SkipApply(string why)
        {
            LogSkip("non applicato", why);
        }

        /// <summary>
        /// Una riga `non inoltrato` / `non applicato`, al massimo una al secondo: i colpi scartati
        /// possono essere frequenti (trappole addosso al proxy, colpi con un attaccante che non e' un
        /// nemico) e non sono la misura del fix. La misura sono le altre due righe.
        /// </summary>
        private static void LogSkip(string verb, string why)
        {
            try
            {
                if (Time.time - _lastSkipLog < SKIP_LOG_INTERVAL)
                    return;

                _lastSkipLog = Time.time;
                ModLog.Info($"{Marker} {verb}: {why}");
            }
            catch
            {
            }
        }
    }
}
