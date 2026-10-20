namespace Blasphemous.Multiplayer.Client.Network
{
    /// <summary>
    /// ⭐⭐ LA CLASSE DI UN MESSAGGIO SUL FILO — cio' che `ICoopTransport.Send` non diceva.
    ///
    /// PERCHE' ESISTE, in una riga: **il tipo del messaggio non basta a dedurre la classe**. La tabella
    /// di `docs\STEAM_INVITES.md` §10 lo dice con tre casi che sarebbero tutti sbagliati:
    ///   · `NetworkType.Attack` e' un **demultiplexer**: cinque messaggi, due classi (`EnemyHit` e
    ///     `PauseRelay` sono affidabili, `CoopEffectRelay` e `SyncIdList` no);
    ///   · `NetworkType.Effect` ha **due classi**: 29 effetti sono cosmetici, ma i tre STOP latchati
    ///     (`TrailOff`, `DashSoundStop`, `ParryHitStop`) sono affidabili, perche' il ricevente non ha
    ///     nessun fusibile che li scada;
    ///   · `NetworkType.Animation` e' **un byte unreliable su un messaggio reliable**: le clip cambiano
    ///     a ogni cambio e la successiva sovrascrive la precedente, ma il byte di MORTE arriva una volta
    ///     sola per morte e senza di lui il cadavere del peer resta in piedi.
    /// ⇒ la classe la sceglie **il punto in cui il payload nasce**, non il tipo. Il trasporto TCP la
    ///   ignora (una riga di commento: c'e' un solo flusso ordinato e affidabile), il P2P la usa per
    ///   scegliere il canale: `Unreliable` -> canale 0 (`k_EP2PSendUnreliableNoDelay`), `Reliable` ->
    ///   canale 1 (`k_EP2PSendReliable`), e le skin (grasse) -> canale 2, affidabile ma isolato.
    ///
    /// ⚠️ I VALORI SONO ESPLICITI E NON SI CAMBIANO: `Unreliable = 0` e' "nessuna garanzia", che e' il
    /// default prudente di chi dimentica di passarla, e `Reliable = 1`. Il byte non finisce MAI sul filo
    /// del protocollo (che resta 11): viaggia solo nell'intestazione del trasporto P2P o nel canale.
    /// </summary>
    internal enum TransportClass : byte
    {
        /// <summary>Puo' essere perso: la copia successiva dello stesso tipo lo sovrascrive (§10).</summary>
        Unreliable = 0,

        /// <summary>Non puo' essere perso: nessun messaggio successivo lo ri-stabilisce (§10).</summary>
        Reliable = 1,
    }
}
