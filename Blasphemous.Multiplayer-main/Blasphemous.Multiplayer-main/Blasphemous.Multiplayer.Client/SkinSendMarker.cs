using Blasphemous.ModdingAPI;
using System;
using System.Diagnostics;
using System.Globalization;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⭐ MARKER DELLA MISURA DI UNA SKIN — `[Net] skin:`. SOLO LOG, ZERO COMPORTAMENTO, ZERO PATCH.
    ///
    /// LA DOMANDA CHE CHIUDE, e sono due domande in una:
    ///   1. **quanti pacchetti e quanti byte** manda una skin — cioe' se le skin hanno bisogno di
    ///      frammentazione. Il canale ha UNA testa sola e la sua lunghezza viaggia in un `ushort`
    ///      (`NetworkManager.QueueMesssage`, `NetworkManager.cs:90`): un payload oltre 65535 byte viene
    ///      TRONCATO nella testa e arriva illeggibile. Se una skin ci sta sotto con un margine largo,
    ///      la frammentazione e' lavoro che non serve; se non ci sta, questo marker lo dice PRIMA che
    ///      il PNG sparisca in silenzio dall'altra parte.
    ///   2. **quante volte parte una skin** in una sessione: `volta=` e' l'ordinale, e ogni riga e' un
    ///      invio. I punti di partenza sono due e sono entrambi nel codice: la connessione
    ///      (`Multiplayer.OnConnect`, `Multiplayer.cs:452`) e ogni cambio di palette
    ///      (`GamePatches.cs:16`, postfix di `SetCurrentColorPaletteId`).
    ///
    /// COSA MANDA DAVVERO UNA SKIN, verificato nel codice e non ipotizzato:
    ///   - la "skin" NON e' un insieme di sprite per frame. E' **una sola** immagine: la texture della
    ///     palette (`Core.ColorPaletteManager.GetColorPaletteById(id)` e' uno `Sprite`, e il suo
    ///     `.texture` e' la LUT dei colori applicata al materiale come `_PaletteTex`,
    ///     `OtherPlayerScript.cs:925`; il ricevente la ricostruisce come `Texture2D(256, 1, RGB24)`,
    ///     `PlayerStatus.cs:92`) ⇒ **1 PNG, non N sprite**;
    ///   - se la palette e' ORIGINALE del gioco non parte nessuna immagine: parte solo il NOME
    ///     (`PlayerStatus.IsOriginalSkin`, la lista di `PlayerStatus.cs:109`), pochi byte, e l'altra
    ///     parte la risolve in locale;
    ///   - la spedizione e' **un** messaggio `NetworkType.Skin`: `QueueMesssage` lo incapsula una volta
    ///     sola e `SendQueue` lo scrive nella stessa `Write` degli altri messaggi in coda. Il TCP puo'
    ///     spezzarlo in segmenti da ~1,4 KB (MTU), ma quelli non sono pacchetti del protocollo e il
    ///     ricevente li ricompone: il conteggio che conta e' **1**.
    ///
    /// LA FORMA DELLA RIGA (una riga, sempre la stessa, in un `Select-String` sola):
    ///   `[Net] skin: pacchetti=1 byte=NNN durata=NN.N sprite=1 tipo=png nome=ID volta=N inviato=si`
    ///     `pacchetti=` — messaggi del protocollo prodotti da QUESTA skin (sempre 1: e' la misura);
    ///     `byte=`      — i byte ACCODATI per quella skin, testa compresa (`payload + 3`): e' la cifra
    ///                    che risponde alla domanda sulla frammentazione, e va confrontata con 65535;
    ///     `durata=`    — millisecondi spesi a COSTRUIRE il payload (per una palette non originale e'
    ///                    quasi tutto `EncodeToPNG`): e' il costo che si paga sul frame del mittente;
    ///     `sprite=`    — quante immagini viaggiano: `1` per un PNG, `0` quando viaggia solo il nome.
    ///                    Il campo esiste per rispondere a "un PNG o tanti sprite?" con un numero e non
    ///                    con una frase;
    ///     `tipo=`      — `png` o `nome`: quale dei due rami di `SendSkin` ha preso;
    ///     `nome=`      — l'id della palette, cosi' la riga si legge accanto al cambio fatto in gioco;
    ///     `volta=`     — ordinale dell'invio nella sessione (la seconda domanda del mandato);
    ///     `inviato=`   — `si` se la connessione era su (`QueueMesssage` scarta in silenzio quando e'
    ///                    giu'), `no` se la riga misura una skin che NON e' partita.
    ///   Solo nel caso cattivo, in coda alla riga: ` !! oltre-il-tetto-ushort: la testa mente`.
    ///
    /// ⚠️ COSA NON FA, ed e' la parte che conta: non tocca il payload, non lo taglia, non lo ri-encoda,
    /// non cambia la dimensione ne' il momento della spedizione, non legge la rete, non scrive stato
    /// della mod e non aggiunge nessuna patch Harmony (i bersagli restano quelli che sono). L'unica
    /// cosa che scrive e' `_sends`, un contatore di QUESTO file che serve solo al campo `volta=`.
    ///
    /// ⚠️ IL VOLUME — dichiarato: una riga per skin INVIATA, cioe' alla connessione e a ogni cambio di
    /// palette. Non e' un marker per frame e non e' un marker del canale posizioni.
    ///
    /// ⚠️ `try/catch` SU TUTTO: `Begin` non lancia mai (restituisce `null`), `Report` non lancia mai
    /// (una misura non deve essere la ragione per cui una skin non parte).
    /// </summary>
    internal static class SkinSendMarker
    {
        /// <summary>
        /// Il prefisso della riga. `[Net]` e' nuovo di proposito: non esiste nessun altro marker con
        /// questo prefisso, quindi `Select-String '\[Net\] skin'` prende queste righe e nient'altro.
        /// </summary>
        internal const string Marker = "[Net] skin:";

        /// <summary>
        /// La testa che `NetworkManager.QueueMesssage` antepone a OGNI messaggio: 2 byte di lunghezza
        /// (`ushort`) + 1 byte di tipo (`NetworkType`) ⇒ `NetworkManager.cs:90-91`.
        /// </summary>
        private const int FRAME_HEAD = 3;

        /// <summary>
        /// Il tetto del canale: la lunghezza di `QueueMesssage` e' un `ushort`, quindi 65535 byte di
        /// payload. Oltre, la testa dice un numero piu' piccolo del vero e il ricevente legge il
        /// messaggio successivo dentro questo. E' la ragione per cui questo marker esiste.
        /// </summary>
        private const int MAX_PAYLOAD = 65535;

        /// <summary>Quante skin sono partite da quando il processo e' vivo. Solo per il campo `volta=`.</summary>
        private static int _sends;

        /// <summary>
        /// Apre la misura. Restituisce `null` se qualcosa va storto: il chiamante prosegue comunque,
        /// senza cronometro, e la riga esce con `durata=-1`.
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
        /// Scrive la riga. Legge soltanto cio' che le viene passato: il payload e' gia' stato accodato
        /// dal chiamante prima di questa chiamata, quindi qui non si decide e non si spedisce niente.
        /// </summary>
        /// <param name="timer">Il cronometro aperto da <see cref="Begin"/> (puo' essere `null`).</param>
        /// <param name="skinName">L'id della palette.</param>
        /// <param name="original">`true` se e' una palette originale (viaggia il nome, non il PNG).</param>
        /// <param name="payload">I byte del payload, senza testa.</param>
        /// <param name="connected">Lo stato della connessione AL MOMENTO dell'invio.</param>
        internal static void Report(Stopwatch timer, string skinName, bool original, int payload,
                                    bool connected)
        {
            try
            {
                _sends++;

                // La durata si legge UNA volta sola: `Elapsed` su un cronometro fermo e' stabile, ma
                // questa riga non deve dipendere da quante volte lo si interroga.
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

                // ⚠️ Solo nel caso cattivo, un campo in piu': se il payload supera il `ushort` la skin
                // che parte NON e' la skin che arriva, e chi legge deve vederlo sulla stessa riga.
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
                // Una misura non deve mai essere la ragione per cui qualcosa si rompe.
            }
        }
    }
}
