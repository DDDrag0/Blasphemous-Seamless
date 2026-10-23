using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.GameControllers.Entities;
using Gameplay.GameControllers.Penitent;
using Gameplay.GameControllers.Penitent.Damage;
using System;
using System.Globalization;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// ⭐ MARKER DELL'IMPORTO DEL DANNO — `[DannoVita]`. SOLO LOG, ZERO COMPORTAMENTO, ZERO PATCH.
    ///
    /// LA LACUNA CHE CHIUDE (`scratch\CONFERMA-RELAY.md` §2.1 e §7.1-buco-1): il relay degli effetti
    /// CONSEGNA e l'area FERISCE — ma **di quanto** non lo diceva nessun marker. Nel log della coppia
    /// di conferma: **176 PV persi in due (88 + 88) e UNA sola riga che li documenta** (`[DannoPeer]
    /// APPLICATO`, G2:1135, `vitaPrima=54 vitaDopo=44 perso=10`). Il resto era invisibile perche'
    /// `[DannoPeer]` stampa la vita **solo per il canale nemico**: quando l'attaccante e' un effetto
    /// (`CorpsesShockwave`, `MaceImpact`, una trappola, un proiettile) quel canale **scarta** il colpo
    /// *prima* di applicarlo — `AttackingEntity` non e' un `Enemy`, vedi `EnemyHitPeer.AttackerOf` — e
    /// il danno si risolve per la strada locale del gioco, che non aveva nessun marker.
    ///
    /// ⚠️ E LA SECONDA LACUNA, quella che nessun marker poteva chiudere da solo: **di chi e' la vita che
    /// scende**. L'evento audio del gioco (`PenitentMagicDamage`) non distingue il `Penitent` LOCALE dal
    /// proxy dell'altro giocatore, e le due pedine stanno a **1,1 unita'** ⇒ **la geometria non dice
    /// quale dei due ha perso vita** (`CONFERMA-RELAY.md` §7.2). Questo marker non ha quel dubbio per
    /// costruzione: e' agganciato alla `PenitentDamageArea`, cioe' alla hurtbox che il danno lo
    /// APPLICA, e stampa `su=locale` / `su=altro(nome)` confrontando l'oggetto colpito con
    /// `Core.Logic.Penitent`. La riga dice **quale** Penitente ha perso vita, non solo quanta.
    ///
    /// ⚠️ PERCHE' NON UNA PATCH HARMONY — e non e' pigrizia, e' la parte che rende questo marker
    /// gratuito: la `PenitentDamageArea` espone GIA' due delegate statici pubblici
    /// (`OnHitGlobal` PRIMA dei cancelli, `OnDamagedGlobal` DOPO l'applicazione,
    /// `Assembly-CSharp.cs:145356-145358`, invocati a `:145594` e `:145602`), e `RaiseDamageEvent` e'
    /// l'UNICO punto in cui il gioco sottrae vita a un `Penitent` da una hurtbox (`:145586`
    /// `_penitent.Damage(num2, hit.HitSoundId)`). ⇒ iscriversi basta, **i bersagli Harmony restano
    /// 269** e non c'e' nessun `Prefix` che possa sbagliare a legare i parametri.
    ///
    /// LA FORMA DELLA RIGA, e perche' quei campi (una riga, sempre la stessa):
    ///   `vitaPrima=` / `vitaDopo=` / `perso=`  — LA richiesta del mandato, **la stessa terna che
    ///                                            `[DannoPeer] APPLICATO` stampa per il canale nemico**;
    ///   `da=`                                  — il nome dell'attaccante: e' cio' che lega i PV a un
    ///                                            EFFETTO preciso (`CorpsesShockwave(Clone)`);
    ///   `classe=`                              — `nemico` / `effetto` / `sconosciuto`, cioe' la stessa
    ///                                            distinzione che fa `EnemyHitPeer.AttackerOf`: e' il
    ///                                            campo che rende la riga filtrabile, e che dice se
    ///                                            questo colpo poteva passare dal canale nemico;
    ///   `danno=` / `tipo=` / `elem=`           — l'importo DICHIARATO dal colpo accanto a quello
    ///                                            EFFETTIVAMENTE perso: se i due divergono, la
    ///                                            differenza e' riduzione, stocks o saturazione a 0;
    ///   `su=`                                  — `locale` o `altro(nome)`: risponde al "quale dei
    ///                                            due" (§7.2) senza dedurlo dalla distanza.
    ///
    /// ⚠️ COSA NON FA: non scrive stato (nessuna vita, nessun flag, nessun `_lastSent`), non decide
    /// niente, non chiama `Kill`/`Damage`/`Death`/`SetHealth`/`Init`, e non tocca nessun marker
    /// esistente. `_before` e' l'unica cosa che scrive, ed e' una variabile di QUESTO file, letta un
    /// istante dopo dallo stesso giro di chiamata.
    ///
    /// ⚠️ IL VOLUME — dichiarato, perche' e' l'unico modo in cui un marker puo' dare fastidio: una riga
    /// per **colpo applicato**, e i colpi applicati sono limitati dalle cornici di invulnerabilita' del
    /// gioco (`DeltaRecoverTime &lt; RecoverTime` in `PenitentDamageArea.TakeDamage`, `:145499`): al
    /// massimo ~2 al secondo **mentre si sta prendendo danno**, zero quando non se ne prende. Non e' un
    /// marker per frame e non e' un marker per hit rifiutato: `OnDamagedGlobal` non viene invocato
    /// quando un cancello ferma il colpo. Un colpo fermato non e' un importo, e non ha una riga.
    /// </summary>
    internal static class PenitentLifeMarker
    {
        /// <summary>
        /// Il prefisso della riga. Distinto da `[DannoPeer]` (il canale nemico) e da `[Danno]` (la vita
        /// del BOSS) di proposito: tre misure diverse non devono confondersi in un `Select-String`.
        /// </summary>
        internal const string Marker = "[DannoVita]";

        /// <summary>
        /// L'iscrizione e' UNA sola per processo: due iscrizioni darebbero due righe per colpo. Se
        /// l'iscrizione fallisce si riprova al giro dopo (`Arm` e' idempotente e viene richiamata da
        /// `Multiplayer.OnInitialize`, che gira una volta).
        /// </summary>
        private static bool _armed;

        /// <summary>La vita letta in `OnHitGlobal`, cioe' PRIMA che il colpo venga applicato.</summary>
        private static float _before;

        /// <summary>
        /// Vero quando `_before` appartiene al colpo in corso. Serve perche' `OnHitGlobal` viene
        /// invocato anche per i colpi che i cancelli fermano: uno di quelli lascerebbe `_before`
        /// vecchio, e senza questo flag la riga successiva stamperebbe un "prima" sbagliato.
        /// </summary>
        private static bool _beforeValid;

        /// <summary>
        /// Si iscrive ai due delegate della hurtbox del `Penitent`. Chiamata da
        /// `Multiplayer.OnInitialize`. Non lancia mai: se i delegate non fossero disponibili, il marker
        /// resta spento e **il gioco non se ne accorge**.
        /// </summary>
        internal static void Arm()
        {
            if (_armed)
                return;

            try
            {
                PenitentDamageArea.OnHitGlobal += OnHitGlobal;
                PenitentDamageArea.OnDamagedGlobal += OnDamagedGlobal;
                _armed = true;
            }
            catch (Exception e)
            {
                _armed = false;
                Fail("iscrizione", e);
            }
        }

        /// <summary>Si disiscrive. Chiamata da `Multiplayer.OnDispose`: nessun delegate resta appeso.</summary>
        internal static void Disarm()
        {
            if (!_armed)
                return;

            try
            {
                PenitentDamageArea.OnHitGlobal -= OnHitGlobal;
                PenitentDamageArea.OnDamagedGlobal -= OnDamagedGlobal;
            }
            catch (Exception e)
            {
                Fail("disiscrizione", e);
            }

            _armed = false;
            _beforeValid = false;
        }

        /// <summary>
        /// Il colpo e' arrivato alla hurtbox: si prende la vita PRIMA. Qui e' l'unico punto in cui
        /// "prima" e' ancora "prima": `RaiseDamageEvent` sottrae la vita poche istruzioni dopo.
        /// </summary>
        private static void OnHitGlobal(Penitent penitent, Hit hit)
        {
            try
            {
                _beforeValid = false;
                if (penitent == null || penitent.Stats == null || penitent.Stats.Life == null)
                    return;

                _before = penitent.Stats.Life.Current;
                _beforeValid = true;
            }
            catch (Exception e)
            {
                Fail("lettura vita prima", e);
            }
        }

        /// <summary>
        /// Il colpo e' stato APPLICATO: si stampa la terna e il resto. Una riga sola, e non lancia mai.
        ///
        /// ⚠️ `OnDamagedGlobal` non vuol dire "il colpo e' passato": vuol dire "la vita e' stata
        /// sottratta". Per questo la riga si chiama `applicato` (il fatto misurabile) e non `ricevuto`
        /// (l'intenzione) — la stessa scelta di `[DannoPeer] APPLICATO`.
        /// </summary>
        private static void OnDamagedGlobal(Penitent damaged, Hit hit)
        {
            try
            {
                if (damaged == null || damaged.Stats == null || damaged.Stats.Life == null)
                    return;

                float after = damaged.Stats.Life.Current;
                if (!_beforeValid)
                {
                    // Nessun "prima" per questo colpo: meglio una riga con `?` che una riga inventata.
                    ModLog.Info($"{Marker} applicato · vitaPrima=? · vitaDopo={F0(after)} · perso=? · " +
                                $"da={Name(hit)} · classe={ClassOf(hit)} · danno={F1(hit)} · " +
                                $"tipo={TypeOf(hit)} · elem={ElementOf(hit)} · su={Side(damaged)}");
                    return;
                }

                _beforeValid = false;

                ModLog.Info($"{Marker} applicato · vitaPrima={F0(_before)} · vitaDopo={F0(after)} · " +
                            $"perso={F0(Math.Max(0f, _before - after))} · da={Name(hit)} · " +
                            $"classe={ClassOf(hit)} · danno={F1(hit)} · tipo={TypeOf(hit)} · " +
                            $"elem={ElementOf(hit)} · su={Side(damaged)}");
            }
            catch (Exception e)
            {
                Fail("riga", e);
            }
        }

        /// <summary>
        /// Il nome dell'attaccante, ripulito dal suffisso d'istanza di Unity: `CorpsesShockwave(Clone)`
        /// e `CorpsesShockwave(Clone) (1)` sono lo stesso prefab, e la riga deve dire QUALE effetto.
        /// </summary>
        private static string Name(Hit hit)
        {
            try
            {
                if (hit.AttackingEntity == null)
                    return "(nessuno)";

                string name = hit.AttackingEntity.name;
                if (string.IsNullOrEmpty(name))
                    return "(senza nome)";

                return name.Replace("(Clone)", string.Empty).Trim();
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// `nemico` / `effetto` / `sconosciuto` — lo STESSO test di `EnemyHitPeer.AttackerOf`
        /// (`GetComponentInParent&lt;Enemy&gt;`), e nella stessa direzione: se qui esce `effetto`, il
        /// canale nemico quel colpo l'ha scartato, e questa riga e' l'unica che ne misura l'importo.
        /// </summary>
        private static string ClassOf(Hit hit)
        {
            try
            {
                if (hit.AttackingEntity == null)
                    return "sconosciuto";

                return hit.AttackingEntity.GetComponentInParent<Enemy>() != null ? "nemico" : "effetto";
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// `locale` se la hurtbox che ha applicato il danno e' quella del `Penitent` di QUESTA
        /// macchina; altrimenti `altro(nome)`. E' il campo che chiude il dubbio del §7.2 della
        /// conferma: non si deduce dalla posizione, si legge dall'oggetto.
        /// </summary>
        private static string Side(Penitent damaged)
        {
            try
            {
                Penitent local = Core.Logic == null ? null : Core.Logic.Penitent;
                if (local == null)
                    return damaged != null && damaged.name != null ? "altro(" + damaged.name + ")" : "?";

                return ReferenceEquals(damaged, local) ? "locale" : "altro(" + damaged.name + ")";
            }
            catch
            {
                return "?";
            }
        }

        private static string TypeOf(Hit hit)
        {
            try
            {
                return hit.DamageType.ToString();
            }
            catch
            {
                return "?";
            }
        }

        private static string ElementOf(Hit hit)
        {
            try
            {
                return hit.DamageElement.ToString();
            }
            catch
            {
                return "?";
            }
        }

        private static string F1(Hit hit)
        {
            try
            {
                return hit.DamageAmount.ToString("F1", CultureInfo.InvariantCulture);
            }
            catch
            {
                return "?";
            }
        }

        /// <summary>
        /// I numeri in una riga di log, con la virgola decimale INGLESE: su una macchina in italiano
        /// `F0` scriverebbe `13` ma una coordinata scriverebbe `-812,3`, e due macchine i cui log vanno
        /// confrontati devono scrivere gli stessi numeri nello stesso modo (la lezione di
        /// `EnemyHitPeer.Pos`).
        /// </summary>
        private static string F0(float value)
        {
            return value.ToString("F0", CultureInfo.InvariantCulture);
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
                // Un marker che esplode mentre riporta di essere esploso non deve poter fermare nulla.
            }
        }
    }
}
