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
    /// <summary>
    /// Intercepts PoolManager object creation to broadcast visual and attack effects to other players.
    /// Uses a receiver-side filtering architecture: the sender broadcasts the prefab name, position, 
    /// and rotation for every relevant instantiation. The receiver decides whether to spawn it by checking:
    /// 1. Resolution: Can it find the requested prefab in the local scene? (checks boss, enemies, penitent, cache).
    /// 2. Deduplication: Has this effect already been instantiated recently at this exact position?
    /// </summary>
    [HarmonyPatch(typeof(PoolManager), nameof(PoolManager.ReuseObject))]
    internal static class EffectRelayPoolHook
    {
        /// <summary>
        /// Postfix on the game's sole pool instantiation method. 
        /// Uses Postfix to observe creations without preventing the local game from functioning natively.
        /// Parameter names must exactly match the original signature for Harmony injection.
        /// </summary>
        [HarmonyPostfix]
        private static void Postfix(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            EffectRelay.OnPoolSpawn(prefab, position, rotation);
        }
    }

    internal static class EffectRelay
    {
        internal const string Marker = "[FxRelay]";

        internal const string VerbSend = "mando";
        internal const string VerbInstantiate = "istanzio";
        internal const string VerbDedup = "salto per dedup";
        internal const string VerbSkip = "not applied";
        internal const string VerbBrake = "freno";
        internal const string VerbCatalog = "catalogo";

        internal const string VerbResolved = "RISOLTO";
        internal const string VerbUnresolved = "UNRESOLVED";

        // Source labels for log keys. Must not be translated as they are grep targets.
        internal const string SourceBoss = "boss";
        internal const string SourceEnemies = "nemici";
        internal const string SourcePenitent = "penitente";
        internal const string SourceCache = "cache";

        /// <summary>
        /// Time window (in seconds) to consider an effect a duplicate if spawned at the same coordinates.
        /// Covers network latency and Penitent invulnerability frames.
        /// </summary>
        private const float DEDUP_SECONDS = 0.5f;

        /// <summary>
        /// Hard cap on effect broadcasts per second to prevent network saturation.
        /// </summary>
        private const int MAX_SENDS_PER_SECOND = 20;

        /// <summary>
        /// Default instances to create if the receiver doesn't have an active pool for the requested prefab.
        /// Set > 1 so rapid consecutive areas don't reuse and abruptly move the same single object.
        /// </summary>
        private const int DEFAULT_POOL_SIZE = 4;

        private const float CATALOG_FAST_SECONDS = 2f;
        private const float CATALOG_SLOW_SECONDS = 10f;
        private const float LOG_INTERVAL = 1f;
        private const int SEEN_SWEEP_AT = 512;
        private const int MAX_CATALOG_NAMES_LOGGED = 12;

        private const int MAX_BOSS_COMPONENTS = 96;
        private const int MAX_ENEMY_COMPONENTS = 64;
        private const int MAX_PENITENT_COMPONENTS = 128;

        private const int MAX_ENEMIES_PER_SEARCH = 32;

        /// <summary>
        /// Seconds to wait before attempting wide reflection again for an unresolved prefab name.
        /// Prevents reflection spam on rapid unknown projectiles while allowing late-spawning enemies.
        /// </summary>
        private const float WIDE_RETRY_SECONDS = 10f;

        private static string _scene = string.Empty;
        private static bool _inLevel;

        /// <summary>Deduplication table: Key -> timestamp of last occurrence.</summary>
        private static readonly Dictionary<string, float> _seen = new Dictionary<string, float>();

        /// <summary>Boss catalog: Prefab Name -> Local GameObject reference.</summary>
        private static readonly Dictionary<string, GameObject> _catalog = new Dictionary<string, GameObject>();

        /// <summary>Wide search cache: Prefab Name -> GameObject reference from non-boss sources.</summary>
        private static readonly Dictionary<string, GameObject> _cache = new Dictionary<string, GameObject>();

        private static readonly Dictionary<string, float> _missAt = new Dictionary<string, float>();
        private static readonly HashSet<int> _harvested = new HashSet<int>();
        private static readonly HashSet<string> _unresolved = new HashSet<string>();
        private static readonly HashSet<string> _reported = new HashSet<string>();

        private static readonly System.Diagnostics.Stopwatch _timer = new System.Diagnostics.Stopwatch();

        private static readonly Dictionary<int, bool> _interesting = new Dictionary<int, bool>();
        private static readonly Dictionary<Type, bool> _typeHasGo = new Dictionary<Type, bool>();
        private static readonly Dictionary<Type, FieldInfo[]> _fieldsOfType = new Dictionary<Type, FieldInfo[]>();

        /// <summary>
        /// Reentrancy guard: prevents an incoming network effect from echoing back out
        /// when it's instantiated via PoolManager locally.
        /// </summary>
        private static bool _applyingFromWire;

        private static float _catalogAt = -1000f;
        private static bool _catalogHasNames;
        private static string _catalogLogged = string.Empty;

        private static int _sendWindowCount;
        private static float _sendWindowAt = -1000f;

        private static float _lastBrakeLog = -1000f;
        private static float _lastSkipLog = -1000f;

        private static bool _penitentHarvested;

        // Diagnostics metrics
        private static int _sends;
        private static int _instantiated;
        private static int _deduped;
        private static int _unresolvedCount;
        private static int _braked;

        private static readonly Dictionary<string, int> _bySource = new Dictionary<string, int>();
        private static readonly Dictionary<string, int> _namesBySource = new Dictionary<string, int>();

        private static int _resolves;
        private static double _resolveTotalMs;
        private static double _resolveMaxMs;

        private static int _wideSearches;
        private static int _wideSkipped;
        private static int _enemiesScanned;

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
                    ModLog.Info($"{Marker} armed scena={_scene} finestraDedup={DEDUP_SECONDS:F1}s " +
                                $"freno={MAX_SENDS_PER_SECOND}/s (receiver filter active: " +
                                $"name→prefab resolution from {SourceBoss}|{SourceEnemies}|" +
                                $"{SourcePenitent}|{SourceCache} + dedup)");
                }
            }
            catch (Exception e)
            {
                Fail("LevelLoaded", e);
            }
        }

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

        /// <summary>
        /// Invoked locally on PoolManager.ReuseObject. Evaluates and potentially broadcasts the instantiation.
        /// </summary>
        internal static void OnPoolSpawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            try
            {
                if (_applyingFromWire || prefab == null)
                    return;

                if (!IsInteresting(prefab))
                    return;

                string name = Clean(prefab.name);
                if (string.IsNullOrEmpty(name))
                    return;

                Note(name, position);

                if (!_inLevel)
                    return;

                if (Main.Multiplayer == null || Main.Multiplayer.NetworkManager == null ||
                    !Main.Multiplayer.NetworkManager.IsConnected)
                    return;

                if (!OtherPlayerHere())
                    return;

                if (Braked(name))
                    return;

                byte[] payload = BuildPayload(name, position, rotation.eulerAngles.z);
                Main.Multiplayer.NetworkManager.SendAttackPayload(payload);

                _sends++;
                ModLog.Info($"{Marker} {VerbSend} n={_sends} prefab=\"{name}\" pos={Pos(position)} " +
                            $"rotZ={F(rotation.eulerAngles.z)} (Attack channel, type " +
                            $"{(byte)AttackType.CoopEffectRelay})");
            }
            catch (Exception e)
            {
                Fail("OnPoolSpawn", e);
            }
        }

        /// <summary>
        /// Records local purely-cosmetic effects in the deduplication table, ensuring that 
        /// if they are ever enabled over the wire, they are correctly deduped against local instantiations.
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
            LogThrottled(ref _lastBrakeLog, $"{Marker} {VerbBrake}: already {MAX_SENDS_PER_SECOND} sends " +
                                            $"this second, dropping (last prefab=\"{name}\", " +
                                            $"scartati finora={_braked})");
            return true;
        }

        /// <summary>
        /// Processes an incoming effect packet. Attempts to resolve the prefab name locally 
        /// and spawns it if not deduped.
        /// </summary>
        internal static void ApplyFromWire(string senderName, byte[] data, int startIdx)
        {
            try
            {
                if (data == null || startIdx < 0 || startIdx + 2 > data.Length)
                {
                    SkipThrottled("packet too short (" +
                                  (data == null ? "null" : data.Length.ToString()) + " bytes)");
                    return;
                }

                int nameLength = data[startIdx + 1];
                int index = startIdx + 2;

                if (data.Length < index + nameLength + 16)
                {
                    SkipThrottled("truncated packet (name " + nameLength + " bytes, " +
                                  (data.Length - index) + " available)");
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
                    SkipThrottled("empty prefab name");
                    return;
                }

                if (!_inLevel)
                {
                    SkipThrottled("outside level (menu or scene change)");
                    return;
                }

                Vector3 position = new Vector3(x, y, z);

                string source;
                double resolveMs;
                GameObject prefab = Resolve(name, out source, out resolveMs);

                if (prefab == null)
                {
                    _unresolvedCount++;
                    if (_unresolved.Add(name))
                    {
                        string penitentState = _penitentHarvested ? "yes" : "no";

                        ModLog.Info($"{Marker} {VerbUnresolved} \"{name}\" tried: " +
                                    $"{SourceBoss},{SourceEnemies},{SourcePenitent} mittente={senderName} " +
                                    $"(in={F3(resolveMs)}ms, catalogo {SourceBoss}={_catalog.Count} nomi, " +
                                    $"{SourceEnemies} scanditi={_enemiesScanned}, {SourcePenitent}={penitentState}, " +
                                    $"cache={_cache.Count}, unresolved so far={_unresolvedCount})");
                    }
                    return;
                }

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
                    Fail("Resolved log line", e);
                }

                string key = Key(name, position);
                float when;
                if (_seen.TryGetValue(key, out when))
                {
                    float age = Time.unscaledTime - when;
                    if (age <= DEDUP_SECONDS)
                    {
                        _deduped++;
                        ModLog.Info($"{Marker} {VerbDedup} prefab=\"{name}\" pos={Pos(position)} " +
                                    $"eta={F(age)}s da={senderName} (local copy already exists: " +
                                    $"dedup totale={_deduped})");
                        return;
                    }
                }

                PoolManager pool = PoolManager.Instance;
                if (pool == null)
                {
                    SkipThrottled("PoolManager missing in this scene");
                    return;
                }

                PoolManager.ObjectInstance instance;
                _applyingFromWire = true;
                try
                {
                    instance = pool.ReuseObject(prefab, position, Quaternion.Euler(0f, 0f, rotZ),
                                                true, DEFAULT_POOL_SIZE);
                }
                finally
                {
                    _applyingFromWire = false;
                }

                if (instance == null)
                {
                    SkipThrottled("pool did not return instance for \"" + name + "\"");
                    return;
                }

                Note(name, position);

                _instantiated++;
                ModLog.Info($"{Marker} {VerbInstantiate} prefab=\"{name}\" pos={Pos(position)} " +
                            $"rotZ={F(rotZ)} da={senderName} (local copy from pool: effect now belongs " +
                            $"to THIS machine; istanziati={_instantiated})");
            }
            catch (Exception e)
            {
                _applyingFromWire = false;
                Fail("ApplyFromWire", e);
            }
        }

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

        /// <summary>
        /// Resolves a prefab name to a GameObject by falling back through 4 localized layers:
        /// Boss catalog > Enemies cache > Penitent cache > Global cache. 
        /// Always prefers the first match.
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
                Fail("Resolve", e);
                prefab = null;
            }

            _timer.Stop();
            ms = _timer.Elapsed.TotalMilliseconds;

            _resolves++;
            _resolveTotalMs += ms;
            if (ms > _resolveMaxMs)
                _resolveMaxMs = ms;

            return prefab;
        }

        private static GameObject ResolveCore(string name, ref string source)
        {
            GameObject prefab;

            if (_catalog.TryGetValue(name, out prefab) && prefab != null)
            {
                source = SourceBoss;
                return prefab;
            }

            if (Time.unscaledTime - _catalogAt >= CATALOG_FAST_SECONDS)
            {
                BuildCatalog();

                if (_catalog.TryGetValue(name, out prefab) && prefab != null)
                {
                    source = SourceBoss;
                    return prefab;
                }
            }

            if (_cache.TryGetValue(name, out prefab))
            {
                if (prefab != null)
                {
                    source = SourceCache;
                    return prefab;
                }

                _cache.Remove(name);
            }

            float now = Time.unscaledTime;
            float missedAt;
            if (_missAt.TryGetValue(name, out missedAt) && now - missedAt < WIDE_RETRY_SECONDS)
            {
                _wideSkipped++;
                return null;
            }

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
            _namesBySource[SourceBoss] = 0;

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

            string signature = Signature();
            if (signature != _catalogLogged)
            {
                _catalogLogged = signature;
                ModLog.Info($"{Marker} {VerbCatalog} scena={_scene} boss={bosses.Count} " +
                            $"nomi={_catalog.Count} [{signature}] (local boss fields)");
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
        /// Recursively reflects the components on the root object to find serialized GameObject assets,
        /// following explicit references specifically inside attack components. Limits depth to prevent overhead.
        /// </summary>
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

                    Component component = value as Component;
                    if (component != null && component is EnemyAttack && reference.Depth < 2)
                    {
                        if (visited.Add(component))
                            queue.Add(new CompRef(component, reference.Depth + 1));
                    }
                }
            }
        }

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

            if (source == null)
                return;

            int seen;
            _namesBySource.TryGetValue(source, out seen);
            _namesBySource[source] = seen + 1;
        }

        /// <summary>
        /// Formats a spatial key used for effect deduplication. 
        /// Coordinates are intentionally rounded to 0 decimal places to unify 
        /// micro-divergences across the network.
        /// </summary>
        private static string Key(string name, Vector3 position)
        {
            return name + "|" + Round(position.x) + "|" + Round(position.y);
        }

        private static string Round(float value)
        {
            double rounded = Math.Round((double)value, 0, MidpointRounding.AwayFromZero);
            if (rounded == 0d)
                rounded = 0d;

            return rounded.ToString("F0", CultureInfo.InvariantCulture);
        }

        private static void Note(string name, Vector3 position)
        {
            if (_seen.Count >= SEEN_SWEEP_AT)
                Sweep();

            _seen[Key(name, position)] = Time.unscaledTime;
        }

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

            _typeHasGo[type] = false; 

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

        /// <summary>
        /// Cleans the prefab name from Unity's instantiation suffixes (e.g. "(Clone)").
        /// Does not strip numeric identifiers that are part of the original prefab name.
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
        /// Forces invariant formatting so logs can be safely grep'd regardless of local OS culture.
        /// </summary>
        private static string Pos(Vector3 position)
        {
            return "(" + F(position.x) + ", " + F(position.y) + ")";
        }

        private static string F(float value)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture);
        }

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

        private static void PrintSummary()
        {
            double average = _resolves > 0 ? _resolveTotalMs / _resolves : 0d;

            ModLog.Info($"{Marker} summary scena={_scene} mandati={_sends} istanziati={_instantiated} " +
                        $"scartatiPerDedup={_deduped} nonRisolti={_unresolvedCount} frenati={_braked} " +
                        $"catalogo={_catalog.Count} risolti=[{Counts(_bySource)}] " +
                        $"nomi=[{Counts(_namesBySource)}] risoluzioni={_resolves} " +
                        $"max={F3(_resolveMaxMs)}ms medio={F3(average)}ms " +
                        $"ricercheLarghe={_wideSearches} saltateDalTetto={_wideSkipped} " +
                        $"nemiciScanditi={_enemiesScanned} nomiNonRisolti={_unresolved.Count}");
        }

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

        private static void Fail(string where, Exception e)
        {
            try
            {
                ModLog.Error($"{Marker} relay failed in {where} (no effect on game): " +
                             e.GetType().Name + ": " + e.Message);
            }
            catch
            {
            }
        }

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