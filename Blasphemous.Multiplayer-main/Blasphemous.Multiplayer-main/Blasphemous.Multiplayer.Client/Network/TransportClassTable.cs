using Blasphemous.Multiplayer.Client.Players;
using Blasphemous.Multiplayer.Client.PvP.Models;
using Blasphemous.Multiplayer.Common.Enums;
using System;

namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// ⭐⭐ LA TABELLA DI `§10` — la classe di un frame, **per i frame che il server genera da solo**.
    ///
    /// PERCHE' SERVE, E PERCHE' NON E' UN DOPPIONE DEI 20 `Send*`. Nel verso peer->host la classe la
    /// sceglie il punto in cui il payload nasce (`NetworkManager`), e l'adapter dell'host la **conserva**
    /// (viaggia nel canale del pacchetto ricevuto). Ma il server manda anche frame che nessun client ha
    /// mai mandato: la risposta `Intro`, il `HostAssign`, il `Connection` di un nuovo arrivato, il replay
    /// al joiner, e soprattutto **`Position`/`Animation`/`Direction` ri-codificati dal suo `PlayerInfo`**
    /// (`Server.cs:488,519,555`). Per quelli non c'e' nessuna classe da conservare: la si ricava dal
    /// frame, con questa tabella. Sono gli stessi tipi e le stesse tre eccezioni di §10.2/§10.3.
    ///
    /// ⚠️ E NON SI INDOVINA: le tre ambiguita' di §10 si risolvono **leggendo il byte che le distingue**,
    /// con lo stesso predicato del mandante:
    ///   · `Animation` -> <see cref="PlayerStatus.IsDeathAnimation"/> (la stessa funzione che usa
    ///     `MainPlayerManager`): le clip normali sono unreliable, il byte di morte e' reliable;
    ///   · `Effect` -> i tre STOP latchati (`TrailOff`, `DashSoundStop`, `ParryHitStop`), che sono gli
    ///     unici che il ricevente non puo' scadere da solo;
    ///   · `Attack` -> il primo byte del payload e' l'`AttackType`: `EnemyHit`/`PauseRelay`/i colpi PvP
    ///     sono reliable, `CoopEffectRelay`/`SyncIdList` no.
    ///
    /// ⚠️ LA FORMA DEL FRAME: `[ushort len][byte type][payload]`, e per i tipi che interessano il
    /// payload comincia col nome del mittente (`[byte nameLen][name...]`, `Server.addPlayerNameToData`).
    /// Su un frame che non si riesce a leggere (troncato, corto) si risponde **`Reliable`**: e' la
    /// scelta prudente — costa banda, non costa stato.
    ///
    /// ⛔ QUESTO FILE NON NOMINA `Steamworks`: e' una tabella, e si prova anche **offline**.
    /// </summary>
    internal static class TransportClassTable
    {
        /// <summary>
        /// La classe di un frame gia' impacchettato. Non lancia: un frame illeggibile vale `Reliable`.
        /// </summary>
        internal static TransportClass ForFrame(byte[] framed)
        {
            byte type;
            int nameOffset;

            if (!TryReadFrameHeader(framed, out type, out nameOffset))
                return TransportClass.Reliable;

            return ForFrame((NetworkType)type, framed, nameOffset);
        }

        /// <summary>
        /// La stessa decisione, ma sul tipo e sul payload gia' separati: `payloadOffset` e' l'indice del
        /// primo byte del payload dentro <paramref name="framed"/>. Serve anche a chi ha gia' letto la
        /// testa (il banco di prova offline).
        /// </summary>
        internal static TransportClass ForFrame(NetworkType type, byte[] framed, int payloadOffset)
        {
            switch (type)
            {
                // 🔵 Unreliable: ogni copia successiva sovrascrive la precedente.
                case NetworkType.Position:
                case NetworkType.Direction:
                case NetworkType.EnemyUpdate:
                case NetworkType.Ping:
                    return TransportClass.Unreliable;

                // ⭐ Animation: un byte unreliable su un messaggio reliable (§10.2), e la morte e'
                // l'eccezione — la stessa funzione che usa il mandante, non una tabella riscritta qui.
                case NetworkType.Animation:
                {
                    byte animation;
                    if (!TryReadByteAfterName(framed, payloadOffset, out animation))
                        return TransportClass.Reliable;

                    return PlayerStatus.IsDeathAnimation(animation)
                        ? TransportClass.Reliable
                        : TransportClass.Unreliable;
                }

                // ⭐ Effect: i 29 cosmetici sono unreliable; i tre STOP latchati no (§10.2).
                case NetworkType.Effect:
                {
                    byte effect;
                    if (!TryReadByteAfterName(framed, payloadOffset, out effect))
                        return TransportClass.Reliable;

                    return IsLatchedStop((EffectType)effect)
                        ? TransportClass.Reliable
                        : TransportClass.Unreliable;
                }

                // ⭐ Attack: il canale e' un demultiplexer, la classe la dice `AttackType` (§10.3).
                case NetworkType.Attack:
                {
                    byte attack;
                    if (!TryReadByteAfterName(framed, payloadOffset, out attack))
                        return TransportClass.Reliable;

                    switch ((AttackType)attack)
                    {
                        case AttackType.CoopEffectRelay:
                        case AttackType.SyncIdList:
                            return TransportClass.Unreliable;

                        default:
                            // EnemyHit, PauseRelay, e ogni colpo PvP (Slash..CanteJondo, NoDamage):
                            // se manca, quello stato resta sbagliato per sempre.
                            return TransportClass.Reliable;
                    }
                }

                // 🟢 Tutto il resto e' reliable (§10.2: fatti durevoli, eventi atomici, handshake).
                default:
                    return TransportClass.Reliable;
            }
        }

        /// <summary>
        /// I tre soli effetti che restano ACCESI per sempre se il loro STOP si perde: la scia fantasma,
        /// il suono del dash e il CLANG della parata. Il ricevente non ha un fusibile che li scada
        /// (`PARRY_HIT_TIMEOUT` e' stato rimosso), quindi la loro perdita lascia lo stato sbagliato.
        /// </summary>
        internal static bool IsLatchedStop(EffectType effect)
        {
            return effect == EffectType.TrailOff
                || effect == EffectType.DashSoundStop
                || effect == EffectType.ParryHitStop;
        }

        /// <summary>
        /// Le tre STOP come numero, per chi ha gia' letto un byte e non vuole costruire un enum.
        /// </summary>
        internal static bool IsLatchedStop(byte effect)
        {
            return IsLatchedStop((EffectType)effect);
        }

        /// <summary>
        /// Legge `[ushort len][byte type]` e restituisce l'indice del primo byte del payload.
        /// `false` se il frame e' piu' corto della sua testa o dichiara piu' byte di quanti ne ha.
        /// </summary>
        internal static bool TryReadFrameHeader(byte[] framed, out byte type, out int payloadOffset)
        {
            type = 0;
            payloadOffset = 0;

            if (framed == null || framed.Length < 3)
                return false;

            int length = framed[0] | (framed[1] << 8);
            if (3 + length > framed.Length)
                return false;

            type = framed[2];
            payloadOffset = 3;
            return true;
        }

        /// <summary>
        /// Il primo byte "utile" dopo il nome del mittente (`[byte nameLen][name...][valore]`).
        /// `false` se il frame e' troppo corto o se il nome dichiarato sborda: in quel caso il
        /// chiamante risponde `Reliable`, che e' la scelta prudente.
        /// </summary>
        internal static bool TryReadByteAfterName(byte[] framed, int payloadOffset, out byte value)
        {
            value = 0;

            if (framed == null || payloadOffset < 0 || payloadOffset >= framed.Length)
                return false;

            int nameLength = framed[payloadOffset];
            int valueOffset = payloadOffset + 1 + nameLength;

            // La lunghezza del nome non puo' portare oltre i byte che ci sono (ne' oltre il frame).
            if (valueOffset >= framed.Length)
                return false;

            value = framed[valueOffset];
            return true;
        }
    }
}
