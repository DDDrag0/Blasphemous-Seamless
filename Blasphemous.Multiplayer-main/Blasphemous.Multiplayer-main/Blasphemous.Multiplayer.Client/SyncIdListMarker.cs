using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⭐ I DUE MARKER CHE MANCAVANO ALLA VOCE 56 — `[Impronta]`. SOLO LOG, ZERO COMPORTAMENTO,
    /// ZERO PATCH, ZERO BERSAGLI HARMONY NUOVI.
    ///
    /// LA VOCE, parola per parola (`docs\TODO.md` §4 del 2026-10-18, tabella B e C): i "2 marker
    /// mancanti (i conteggi del peer)" sono i due pezzi del marker **(3)**, che l'audit del 2026-10-12
    /// elenco' senza provarlo:
    ///   **B (3a)** — *quante `spawn point` e quanti `id` ha L'ALTRO*, accanto ai propri;
    ///   **C (3b)** — *il PRIMO id presente su UN LATO SOLO*, cioe' **quale** id diverge e non solo
    ///                **che** diverge.
    ///
    /// PERCHE' MANCAVANO, e la ragione e' strutturale: sul filo **viaggia solo l'`int` dell'impronta**
    /// (`NetworkManager.SendEnemyUpdate`, un `int` ogni 30 byte di stato), quindi la riga
    /// `IMPRONTA DIVERSA` (`EnemySyncManager.ReceiveEnemyUpdate`) puo' dire **che** le due liste
    /// divergono e i conteggi **solo LOCALI**, ma non puo' dire ne' i conteggi dell'altro ne' quale id
    /// diverge: **quel dato non e' mai arrivato su questa macchina**.
    ///
    /// LA FORMA — richiesta/risposta sul canale `Attack`, che e' l'unico gia' pronto a fare questo
    /// viaggio: il server lo inoltra ai peer della STESSA SCENA escludendo il mittente e ci PREMETTE il
    /// nome del mittente (`Server.cs:676-699`). ⇒ ⛔ **nessuna riga di protocollo** (resta 11),
    /// ⛔ **nessuna riga di server** (che va ripubblicato su entrambe le macchine), ✅ solo un valore
    /// nuovo di `AttackType` (`SyncIdList = 202`), che e' un byte libero dell'enum del CLIENT. Un
    /// client vecchio che riceve 202 non lo trova in `attacks.json` ed **esce** (`AttackManager.cs:31`)
    /// senza eccezioni; un server vecchio inoltra i byte senza interpretarli.
    ///
    /// PERCHE' UNA RICHIESTA E NON DUE `int` IN PIU' SU OGNI PACCHETTO DI STATO: i conteggi (B)
    /// starebbero anche in 8 byte appesi a `SendEnemyUpdate`, ma la LISTA (C) no — viaggerebbe **una
    /// volta per nemico per giro**, cioe' O(N²) byte al secondo per dire una cosa che serve **una volta
    /// ogni 15 s**, e solo quando c'e' una divergenza da spiegare. La richiesta parte **nello stesso
    /// istante in cui la mod stampa `IMPRONTA DIVERSA`** (che e' gia' limitato a una riga ogni 15 s,
    /// `EnemySyncManager.FINGERPRINT_REPORT_COOLDOWN`) ⇒ al massimo **una coppia di pacchetti ogni
    /// 15 s**, e solo mentre la divergenza c'e'.
    ///
    /// ⚠️ NON C'E' NESSUN ANELLO: la RISPOSTA non chiede niente a nessuno. La richiesta non viene mai
    /// mandata da chi riceve una risposta, e la risposta viene mandata solo a chi ha chiesto, una volta.
    /// ⚠️ E IL DATO NON DECIDE NIENTE, come vuole la voce 56: nessuno legge questi numeri per scegliere
    /// cosa fare; nessuna tabella della mod viene toccata, nessun id viene aggiunto o tolto. Sono due
    /// righe di log e una fotografia della lista che la mod ha GIA' in memoria (`_ids`, `_spawnPoints`).
    ///
    /// LA FORMA DEL PAYLOAD (dopo il nome del mittente premesso dal server; `startIdx` = indice del
    /// primo byte del payload vero, cioe' del byte `AttackType`):
    /// <code>
    ///   +0   byte    AttackType.SyncIdList (202) — la testa di OGNI payload di questo canale
    ///   +1   byte    kind (0 = richiesta, 1 = risposta)
    ///   +2   byte    lunghezza del nome (il RICHIEDENTE nella richiesta, il DESTINATARIO nella risposta)
    ///   +3   string  nome (UTF8)
    ///
    ///   solo risposta, dopo il nome:
    ///   ..   int     impronta del rispondente
    ///   ..   int     quanti id ha
    ///   ..   int     quante spawn point ha
    ///   ..   ushort  quanti id seguono
    ///   ..   n x (byte lunghezza + string id UTF8)
    /// </code>
    /// </summary>
    internal static class SyncIdListMarker
    {
        /// <summary>
        /// Il prefisso delle righe. Distinto da `[EnemySync]` (dove vive `IMPRONTA DIVERSA`) di
        /// proposito: la riga vecchia NON viene toccata, e queste due si riconoscono a colpo d'occhio.
        /// </summary>
        internal const string Marker = "[Impronta]";

        private const byte KIND_REQUEST = 0;
        private const byte KIND_REPLY = 1;

        /// <summary>
        /// Lo stesso periodo del referto della divergenza (`FINGERPRINT_REPORT_COOLDOWN = 15f`): una
        /// richiesta per ogni riga `IMPRONTA DIVERSA`, non una in piu'.
        /// </summary>
        private const float REQUEST_COOLDOWN = 15f;

        /// <summary>Un tetto difensivo: una lista piu' lunga di cosi' non e' una scena, e' un guasto.</summary>
        private const int MAX_IDS = 512;

        /// <summary>
        /// Il tetto in BYTE della risposta, sotto il massimo del canale (la lunghezza viaggia in un
        /// `ushort`, quindi 65535 byte): un payload piu' lungo verrebbe troncato nella testa e
        /// arriverebbe illeggibile. Vedi il commento in `AnswerRequest`.
        /// </summary>
        private const int MAX_PAYLOAD = 60000;

        private static float _lastRequestAt = -1000f;
        private static int _comparisons;

        /// <summary>
        /// Chiede all'altro i suoi conteggi e la sua lista. Chiamata dal punto in cui la mod scopre la
        /// divergenza (`EnemySyncManager.ReceiveEnemyUpdate`, subito dopo la riga `IMPRONTA DIVERSA`).
        /// Non lancia mai e non fa niente se non si e' connessi: un marker non deve mai essere la
        /// ragione per cui qualcosa si rompe.
        /// </summary>
        internal static void RequestPeerIdList()
        {
            try
            {
                if (Main.Multiplayer == null || Main.Multiplayer.NetworkManager == null)
                    return;

                if (!Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                if (Time.time - _lastRequestAt < REQUEST_COOLDOWN)
                    return;

                string me = Main.Multiplayer.PlayerName;
                if (string.IsNullOrEmpty(me))
                    return;

                _lastRequestAt = Time.time;

                byte[] name = Encoding.UTF8.GetBytes(me);
                if (name.Length > 255)
                    Array.Resize(ref name, 255);

                List<byte> bytes = new List<byte>(3 + name.Length);

                // ⚠️ IL PRIMO BYTE E' `AttackType.SyncIdList`, NON il tipo di messaggio: il payload del
                // canale Attack e' auto-descritto dal primo byte (`NetworkManager.ReceiveAttack` legge
                // `(AttackType)data[startIdx]` PRIMA di sapere di che verso si tratta), e un payload che
                // cominciasse con `KIND_REQUEST = 0` verrebbe letto come `AttackType.Slash`, cioe' come
                // un colpo PvP. E' la stessa forma di `EnemyHitPeer.BuildPayload` e `EffectRelay`.
                bytes.Add((byte)AttackType.SyncIdList);
                bytes.Add(KIND_REQUEST);
                bytes.Add((byte)name.Length);
                bytes.AddRange(name);

                Main.Multiplayer.NetworkManager.SendAttackPayload(bytes.ToArray());
            }
            catch (Exception e)
            {
                Fail("richiesta", e);
            }
        }

        /// <summary>
        /// Legge un payload del canale `Attack` di tipo `AttackType.SyncIdList`. Chiamata da
        /// `NetworkManager.ReceiveAttack`, prima del ramo PvP, come i due versi che l'hanno preceduta.
        /// Non lancia mai.
        /// </summary>
        internal static void ApplyFromWire(string senderName, byte[] data, int startIdx)
        {
            try
            {
                if (data == null || startIdx < 0 || data.Length < startIdx + 3)
                {
                    Skip("pacchetto troppo corto");
                    return;
                }

                // `data[startIdx]` e' `AttackType.SyncIdList` (il router l'ha gia' verificato) e il
                // primo byte DOPO e' il tipo di messaggio.
                byte kind = data[startIdx + 1];
                int index = startIdx + 2;
                int nameLength = data[index];
                index++;

                if (data.Length < index + nameLength)
                {
                    Skip("nome troncato");
                    return;
                }

                string name = Encoding.UTF8.GetString(data, index, nameLength);
                index += nameLength;

                if (kind == KIND_REQUEST)
                {
                    AnswerRequest(senderName, name);
                    return;
                }

                if (kind == KIND_REPLY)
                {
                    ReadReply(senderName, name, data, index);
                    return;
                }

                Skip("tipo di messaggio sconosciuto (" + kind + ")");
            }
            catch (Exception e)
            {
                Fail("lettura", e);
            }
        }

        /// <summary>
        /// E' arrivata una richiesta: si risponde con la fotografia della PROPRIA lista, e si stampa che
        /// si e' risposto (una riga: senza, dall'altra parte un silenzio sarebbe indistinguibile da un
        /// pacchetto perso). Nessuna condizione, nessun confronto: chi chiede ha gia' visto la
        /// divergenza, e una risposta in piu' non fa danni.
        /// </summary>
        private static void AnswerRequest(string senderName, string requester)
        {
            try
            {
                string me = Main.Multiplayer == null ? null : Main.Multiplayer.PlayerName;
                if (string.IsNullOrEmpty(me) || requester != me)
                    return; // la richiesta non e' per me: il canale Attack e' un broadcast di scena

                if (Main.Multiplayer.NetworkManager == null || !Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                int spawnPoints;
                int ids;
                int fingerprint;
                List<string> list;
                EnemySyncManager.MarkerSnapshot(out spawnPoints, out ids, out fingerprint, out list);

                byte[] target = Encoding.UTF8.GetBytes(senderName ?? string.Empty);
                if (target.Length > 255)
                    Array.Resize(ref target, 255);

                // Si codificano prima gli id, poi si decide quanti ne entrano: il conteggio va scritto
                // PRIMA delle voci, quindi non si puo' scoprire "a meta'" che il tetto e' stato superato.
                List<byte[]> encoded = new List<byte[]>(list.Count);
                for (int i = 0; i < list.Count && encoded.Count < MAX_IDS; i++)
                {
                    byte[] id = Encoding.UTF8.GetBytes(list[i] ?? string.Empty);
                    if (id.Length > 255)
                        Array.Resize(ref id, 255);

                    encoded.Add(id);
                }

                int head = 3 + target.Length + 14;

                // ⚠️ IL TETTO E' SUI BYTE, NON SUL NUMERO: `QueueMesssage` scrive la lunghezza del
                // pacchetto in un `ushort` (`NetworkManager.cs:90`), quindi un payload oltre 65535 byte
                // verrebbe TRONCATO nella testa e arriverebbe illeggibile — un marker che rompe il
                // canale invece di misurarlo. Un id e' lungo ~40 byte e una stanza ne ha decine, quindi
                // il caso non si presenta; il controllo c'e' perche' un caso che "non si presenta" non e'
                // un caso escluso, ed e' esattamente il modo in cui i marker fanno danni.
                int sent = 0;
                int total = head + 2;
                for (int i = 0; i < encoded.Count; i++)
                {
                    if (total + 1 + encoded[i].Length > MAX_PAYLOAD)
                        break;

                    total += 1 + encoded[i].Length;
                    sent++;
                }

                List<byte> bytes = new List<byte>(total);
                bytes.Add((byte)AttackType.SyncIdList); // vedi la nota in RequestPeerIdList: e' la testa
                bytes.Add(KIND_REPLY);
                bytes.Add((byte)target.Length);
                bytes.AddRange(target);
                bytes.AddRange(BitConverter.GetBytes(fingerprint));
                bytes.AddRange(BitConverter.GetBytes(ids));
                bytes.AddRange(BitConverter.GetBytes(spawnPoints));
                bytes.AddRange(BitConverter.GetBytes((ushort)sent));

                for (int i = 0; i < sent; i++)
                {
                    bytes.Add((byte)encoded[i].Length);
                    bytes.AddRange(encoded[i]);
                }

                Main.Multiplayer.NetworkManager.SendAttackPayload(bytes.ToArray());

                ModLog.Info($"{Marker} richiesta da={senderName} · risposta inviata: " +
                            $"spawn={spawnPoints} id={ids} impronta={fingerprint} idTrasmessi={sent}");
            }
            catch (Exception e)
            {
                Fail("risposta", e);
            }
        }

        /// <summary>
        /// E' arrivata la risposta: si confrontano le due liste e si stampano **i due conteggi accanto
        /// ai propri** e **il primo id presente su un lato solo**, che sono esattamente B e C.
        /// </summary>
        private static void ReadReply(string senderName, string target, byte[] data, int index)
        {
            try
            {
                string me = Main.Multiplayer == null ? null : Main.Multiplayer.PlayerName;
                if (string.IsNullOrEmpty(me) || target != me)
                    return;

                if (data.Length < index + 12)
                {
                    Skip("risposta troncata (testa)");
                    return;
                }

                int peerFingerprint = BitConverter.ToInt32(data, index);
                int peerIds = BitConverter.ToInt32(data, index + 4);
                int peerSpawnPoints = BitConverter.ToInt32(data, index + 8);
                index += 12;

                if (data.Length < index + 2)
                {
                    Skip("risposta troncata (conteggio id)");
                    return;
                }

                int count = BitConverter.ToUInt16(data, index);
                index += 2;

                List<string> peerList = new List<string>(Math.Min(count, MAX_IDS));
                for (int i = 0; i < count && i < MAX_IDS; i++)
                {
                    if (data.Length < index + 1)
                    {
                        Skip("risposta troncata (id " + i + " di " + count + ")");
                        break;
                    }

                    int length = data[index];
                    index++;

                    if (data.Length < index + length)
                    {
                        Skip("id " + i + " di " + count + " troncato");
                        break;
                    }

                    peerList.Add(Encoding.UTF8.GetString(data, index, length));
                    index += length;
                }

                int localSpawnPoints;
                int localIds;
                int localFingerprint;
                List<string> localList;
                EnemySyncManager.MarkerSnapshot(out localSpawnPoints, out localIds, out localFingerprint, out localList);

                // ⭐ C — il PRIMO id presente su un lato solo, nelle due direzioni. Il confronto e'
                // lineare sulle liste ordinate (`_ids.Sort(string.CompareOrdinal)` in OnEnemiesSpawned),
                // quindi "primo" e' anche "il primo in ordine di scena", non "il primo che capita".
                string firstOnlyPeer = FirstMissing(peerList, localList);
                string firstOnlyLocal = FirstMissing(localList, peerList);

                _comparisons++;

                ModLog.Info($"{Marker} confronto #{_comparisons} · peer={senderName} · " +
                            $"spawn: peer={peerSpawnPoints} locale={localSpawnPoints} · " +
                            $"id: peer={peerIds} locale={localIds} · " +
                            $"impronta: peer={peerFingerprint} locale={localFingerprint} · " +
                            $"primoIdSoloPeer={Show(firstOnlyPeer)} · " +
                            $"primoIdSoloLocale={Show(firstOnlyLocal)} · " +
                            $"idConfrontati: peer={peerList.Count} locale={localList.Count}");
            }
            catch (Exception e)
            {
                Fail("confronto", e);
            }
        }

        /// <summary>
        /// Il primo elemento di <paramref name="first"/> che non e' in <paramref name="second"/>.
        /// `null` se non ce n'e' nessuno, cioe' se `first` e' un sottoinsieme di `second`.
        /// </summary>
        private static string FirstMissing(List<string> first, List<string> second)
        {
            if (first == null || second == null)
                return null;

            HashSet<string> index = new HashSet<string>(second);
            for (int i = 0; i < first.Count; i++)
            {
                string id = first[i];
                if (id != null && !index.Contains(id))
                    return id;
            }

            return null;
        }

        /// <summary>Un campo che deve dire anche "non c'e' niente da dire": `nessuno` e' un reperto.</summary>
        private static string Show(string id)
        {
            return string.IsNullOrEmpty(id) ? "(nessuno)" : "\"" + id + "\"";
        }

        private static void Skip(string why)
        {
            try
            {
                ModLog.Info($"{Marker} non letto: {why}");
            }
            catch
            {
            }
        }

        private static void Fail(string what, Exception e)
        {
            try
            {
                ModLog.Error($"{Marker} {what} fallita ({e.GetType().Name}): {e.Message} - " +
                             "il marker tace, il gioco no");
            }
            catch
            {
            }
        }
    }
}
