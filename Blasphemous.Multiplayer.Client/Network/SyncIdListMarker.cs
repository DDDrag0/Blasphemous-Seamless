using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.PvP.Models;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Diagnostic markers ([Impronta]) for the id-list comparison between peers. Uses the Attack
    /// channel with a new AttackType (SyncIdList = 202): the server already forwards it to scene
    /// peers, so no protocol or server change is needed. Answers two questions the wire cannot -
    /// the peer's spawn/id counts alongside ours, and the first id present on one side only.
    /// Log only: no state, no decisions.
    /// </summary>
    internal static class SyncIdListMarker
    {
        /// <summary>Log prefix, distinct from [EnemySync] where IMPRONTA DIVERSA lives.</summary>
        internal const string Marker = "[Impronta]";

        private const byte KIND_REQUEST = 0;
        private const byte KIND_REPLY = 1;

        /// <summary>One request per IMPRONTA DIVERSA line (same 15s cooldown), not one extra.</summary>
        private const float REQUEST_COOLDOWN = 15f;

        /// <summary>Defensive cap: longer lists mean a fault, not a scene.</summary>
        private const int MAX_IDS = 512;

        /// <summary>Payload cap below the ushort length header (larger payloads get truncated).</summary>
        private const int MAX_PAYLOAD = 60000;

        private static float _lastRequestAt = -1000f;
        private static int _comparisons;

        /// <summary>
        /// Asks the peer for its counts and id list. Called right after IMPRONTA DIVERSA is
        /// logged. Never throws; no-op when not connected.
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

                // The first byte is AttackType.SyncIdList, not the message kind: the Attack
                // channel payload is self-described by the first byte, and KIND_REQUEST = 0
                // would otherwise be read as AttackType.Slash.
                bytes.Add((byte)AttackType.SyncIdList);
                bytes.Add(KIND_REQUEST);
                bytes.Add((byte)name.Length);
                bytes.AddRange(name);

                Main.Multiplayer.NetworkManager.SendAttackPayload(bytes.ToArray());
            }
            catch (Exception e)
            {
                Fail("request", e);
            }
        }

        /// <summary>
        /// Handles an Attack payload of type SyncIdList. Called before the PvP branch, like the
        /// other directions. Never throws.
        /// </summary>
        internal static void ApplyFromWire(string senderName, byte[] data, int startIdx)
        {
            try
            {
                if (data == null || startIdx < 0 || data.Length < startIdx + 3)
                {
                    Skip("packet too short");
                    return;
                }

                // data[startIdx] is AttackType.SyncIdList (verified by the router); the next
                // byte is the message kind.
                byte kind = data[startIdx + 1];
                int index = startIdx + 2;
                int nameLength = data[index];
                index++;

                if (data.Length < index + nameLength)
                {
                    Skip("name truncated");
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

                Skip("unknown message kind (" + kind + ")");
            }
            catch (Exception e)
            {
                Fail("read", e);
            }
        }

        /// <summary>
        /// Handles a request: replies with a snapshot of this machine's own list, and logs the
        /// reply so a peer's silence is distinguishable from a lost packet.
        /// </summary>
        private static void AnswerRequest(string senderName, string requester)
        {
            try
            {
                string me = Main.Multiplayer == null ? null : Main.Multiplayer.PlayerName;
                if (string.IsNullOrEmpty(me) || requester != me)
                    return; // the request is not for me: the Attack channel is a scene broadcast

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

                // Encode ids first, then decide how many fit: the count must be written before
                // the entries, so "over the cap halfway through" is not discoverable later.
                List<byte[]> encoded = new List<byte[]>(list.Count);
                for (int i = 0; i < list.Count && encoded.Count < MAX_IDS; i++)
                {
                    byte[] id = Encoding.UTF8.GetBytes(list[i] ?? string.Empty);
                    if (id.Length > 255)
                        Array.Resize(ref id, 255);

                    encoded.Add(id);
                }

                int head = 3 + target.Length + 14;

                // The cap is on BYTES, not count: QueueMesssage writes the payload length in a
                // ushort, so a payload beyond 65535 gets truncated at the header. A marker must
                // not break the channel it measures.
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
                bytes.Add((byte)AttackType.SyncIdList); // see the note in RequestPeerIdList
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

                ModLog.Info($"{Marker} request da={senderName} · reply sent: " +
                            $"spawn={spawnPoints} id={ids} impronta={fingerprint} idTrasmessi={sent}");
            }
            catch (Exception e)
            {
                Fail("reply", e);
            }
        }

        /// <summary>
        /// Handles a reply: prints both sides' counts alongside each other, plus the first id
        /// present on one side only, for both directions.
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
                    Skip("reply truncated (head)");
                    return;
                }

                int peerFingerprint = BitConverter.ToInt32(data, index);
                int peerIds = BitConverter.ToInt32(data, index + 4);
                int peerSpawnPoints = BitConverter.ToInt32(data, index + 8);
                index += 12;

                if (data.Length < index + 2)
                {
                    Skip("reply truncated (id count)");
                    return;
                }

                int count = BitConverter.ToUInt16(data, index);
                index += 2;

                List<string> peerList = new List<string>(Math.Min(count, MAX_IDS));
                for (int i = 0; i < count && i < MAX_IDS; i++)
                {
                    if (data.Length < index + 1)
                    {
                        Skip("reply truncated (id " + i + " of " + count + ")");
                        break;
                    }

                    int length = data[index];
                    index++;

                    if (data.Length < index + length)
                    {
                        Skip("id " + i + " of " + count + " truncated");
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

                // First id present on one side only, both directions. Lists are sorted by scene
                // order (OnEnemiesSpawned), so "first" is scene order, not arrival order.
                string firstOnlyPeer = FirstMissing(peerList, localList);
                string firstOnlyLocal = FirstMissing(localList, peerList);

                _comparisons++;

                ModLog.Info($"{Marker} comparison #{_comparisons} · peer={senderName} · " +
                            $"spawn: peer={peerSpawnPoints} locale={localSpawnPoints} · " +
                            $"id: peer={peerIds} locale={localIds} · " +
                            $"impronta: peer={peerFingerprint} locale={localFingerprint} · " +
                            $"primoIdSoloPeer={Show(firstOnlyPeer)} · " +
                            $"primoIdSoloLocale={Show(firstOnlyLocal)} · " +
                            $"idConfrontati: peer={peerList.Count} locale={localList.Count}");
            }
            catch (Exception e)
            {
                Fail("comparison", e);
            }
        }

        /// <summary>First element of <paramref name="first"/> not in <paramref name="second"/>, or null.</summary>
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

        /// <summary>Field that must render even when absent: nessuno is a datum.</summary>
        private static string Show(string id)
        {
            return string.IsNullOrEmpty(id) ? "(nessuno)" : "\"" + id + "\"";
        }

        private static void Skip(string why)
        {
            try
            {
                ModLog.Info($"{Marker} not read: {why}");
            }
            catch
            {
            }
        }

        private static void Fail(string what, Exception e)
        {
            try
            {
                ModLog.Error($"{Marker} {what} failed ({e.GetType().Name}): {e.Message} - " +
                             "the marker stays silent, the game does not");
            }
            catch
            {
            }
        }
    }
}