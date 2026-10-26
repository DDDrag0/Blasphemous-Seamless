using Blasphemous.ModdingAPI;
using System;
using System.Diagnostics;
using System.Globalization;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// Skin send measurement, log only. Answers two questions: how many bytes a skin uses (does
    /// it fit the ushort length header of the frame, or does it need fragmentation?) and how many
    /// times a skin is sent per session. A skin is a single PNG of the palette texture (or just
    /// the palette name when the skin is original). Never touches the payload, never changes when
    /// or how it is sent, no Harmony patch.
    ///
    /// Line format:
    ///   [Net] skin: pacchetti=1 byte=NNN durata=NN.N sprite=1 tipo=png nome=ID volta=N inviato=si
    /// with ` !! oltre-il-tetto-ushort: la testa mente` appended when the payload exceeds 65535.
    /// </summary>
    internal static class SkinSendMarker
    {
        /// <summary>Line prefix. Unique so Select-String '\ [Net\] skin' picks only these lines.</summary>
        internal const string Marker = "[Net] skin:";

        /// <summary>Frame head added by QueueMesssage: 2 bytes length (ushort) + 1 byte type.</summary>
        private const int FRAME_HEAD = 3;

        /// <summary>
        /// Channel ceiling: QueueMesssage's length is a ushort, so 65535 bytes of payload. Past
        /// that, the head lies and the receiver misreads the next message as part of this one.
        /// </summary>
        private const int MAX_PAYLOAD = 65535;

        /// <summary>Number of skins sent since process start. Used only for the `volta=` field.</summary>
        private static int _sends;

        /// <summary>
        /// Starts the timer. Returns null on any failure: the caller proceeds without a stopwatch
        /// and the line prints `durata=-1`.
        /// </summary>
        internal static Stopwatch Begin()
        {
            try
            {
                return Stopwatch.StartNew();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Writes the line. Reads only what it receives; the payload has already been queued by
        /// the caller. Never throws.
        /// </summary>
        internal static void Report(Stopwatch timer, string skinName, bool original, int payload,
                                    bool connected)
        {
            try
            {
                _sends++;

                double ms = -1d;
                if (timer != null)
                {
                    timer.Stop();
                    ms = timer.Elapsed.TotalMilliseconds;
                }

                int wire = payload + FRAME_HEAD;
                int sprites = original ? 0 : 1;

                string line = string.Format(CultureInfo.InvariantCulture,
                    "{0} pacchetti=1 byte={1} durata={2:0.0} sprite={3} tipo={4} nome={5} volta={6} inviato={7}",
                    Marker,
                    wire,
                    ms,
                    sprites,
                    original ? "nome" : "png",
                    skinName ?? "(nessuno)",
                    _sends,
                    connected ? "si" : "no");

                if (payload > MAX_PAYLOAD)
                {
                    line += string.Format(CultureInfo.InvariantCulture,
                        " !! oltre-il-tetto-ushort: la testa mente (payload {0} > {1})",
                        payload, MAX_PAYLOAD);
                }

                ModLog.Info(line);
            }
            catch
            {
                // A measurement must never be the reason something breaks.
            }
        }
    }
}