using System;
using System.Globalization;
using Blasphemous.ModdingAPI;
using Framework.Managers;
using Gameplay.UI.Others.UIGameLogic;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.Multiplayer.Client
{
    /// <summary>
    /// LA BANDA SULLA BARRA DELLA VITA — la rappresentazione visiva della Colpa sulla vita, cioe' la
    /// forma che mancava (`scratch\REFERTO-COLPA-VITA.md` §7 caso 3: "la barra vita non si accorcia").
    ///
    /// IL FATTO DA CUI PARTE TUTTO, e non e' un'opinione: `PlayerHealth.CalculateHealthBarSize`
    /// (`PlayerHealth.cs:111-128`) scrive `sizeDelta.x = Life.Final` sulle due barre e la riscrive **solo
    /// al cambio** (`:117`), e `Attribute.Final => CalculateValue()` (`Attribute.cs:23`) e' `virtual`, ma
    /// `VariableAttribute.CalculateValue` e' `new virtual` (`VariableAttribute.cs:83`) ⇒ `MaxFactor` NON
    /// entra nella larghezza. Con la colpa a 0,8 la barra resta **larga uguale** e si riempie all'80%: il
    /// buco si vede, la penalita' no. Questa banda disegna quel buco.
    ///
    /// L'AGGANCIO, e perche' e' l'unico senza reflection. `PlayerHealth` ha i due `Image` **privati**
    /// (`:14`, `:20`) e `backgroundFillTransform` privato (`:23`): l'unico handle pubblico e' il
    /// `transform` del componente (`MonoBehaviour.transform`). La singola barra si trova quindi con il
    /// **criterio pubblico** dato dalla sonda (`scratch\SONDA-ROVI-VITA.md` §1):
    ///   · `Image` con `type == Image.Type.Filled`, **e**
    ///   · larghezza ≈ `Life.Final` — misurata su `rect.width` e, in seconda battuta, su `sizeDelta.x`
    ///     (*e' il valore che il gioco scrive davvero a `:123-125`*).
    /// `health` e `loss` sono **geometricamente identiche** (`:123-124`) ⇒ e' indifferente quale si prende.
    /// Se nessuna `Image` passa il criterio la banda **non si disegna**: meglio nessuna banda che una
    /// banda nel posto sbagliato.
    ///
    /// LA GEOMETRIA. La banda e' un `Image` nuovo, figlio del **padre** della barra, e occupa
    /// **`(1 - fattore) × larghezza`** della barra **sul lato destro** (il lato opposto a quello da cui
    /// il riempimento parte: se mai `fillOrigin` fosse destro, la banda va a sinistra — cosi' cade sempre
    /// dalla parte **vuota**, che e' quella che rappresenta la colpa). Le coordinate si ricavano dai
    /// **world corners** della barra convertiti nello spazio del padre: nessuna assunzione su ancore,
    /// pivot o scala del prefab, e nessun `sizeDelta` sottratto a mano.
    /// Figlia del padre e **ultima sorella** di proposito: cosi' sta sopra sia `health` sia `loss`
    /// (la scia del danno), che sono due immagini sovrapposte. Un `LayoutElement.ignoreLayout`
    /// impedisce che un eventuale layout group la sposti.
    ///
    /// IL COLORE — **PROPOSTO E DICHIARATO**: sangue scuro, semitrasparente,
    /// `rgba(0.42, 0.05, 0.07, 0.62)`. Sta sopra la barra senza nascondere il riempimento (si vede
    /// attraverso), e non e' ne' il rosso del danno ne' il bianco della vita: si legge come "questo
    /// pezzo non ti appartiene piu'". `raycastTarget = false`: non ruba un click.
    ///
    /// IL REFRESH, e il punto in cui si sbaglia. `PlayerHealth` e `PlayerFervour` vengono **disabilitati
    /// dal gioco** (`GameplayWidget.cs:103-104`; `PlayerFervour.cs:212`, `:224`, `:229`) ⇒ un refresh
    /// dentro il **loro** `Update` si spegne con loro. Qui non c'e' nessun `Update` nostro e nessuna
    /// patch nuova (`targets` invariati): si riusa il ciclo per frame che la mod ha gia'
    /// (`Multiplayer.OnLateUpdate`, lo stesso di `PauseNotice.Tick()`, che gira anche a `timeScale = 0`),
    /// piu' i **tre eventi** che la colpa ha gia' (spawn / morte / falo'), che forzano un refresh
    /// immediato senza aspettare il tick.
    ///
    /// ⚠️ LA `UI` NON SI RICREA — `GenericElements` e' caricata una volta e mai scaricata
    /// (`LevelManager.cs:111`, `:469`, esclusa dai livelli a `:116`) ⇒ la banda si crea **una volta** e
    /// poi si aggiorna. Il `Penitent` invece **si ricrea** (`SpawnManager.cs:489`), e con lui
    /// `Life.Final` e `Life.MaxFactor`: e' per questo che il refresh c'e' anche a ogni spawn.
    ///
    /// ⚠️ `fattore == 1.0` ⇒ banda **nascosta** (`Image.enabled = false`): nessun residuo visivo dopo il
    /// falo', che e' l'unico azzeramento (`LifeGuilt.ResetAtBonfire`).
    ///
    /// TUTTO E' DENTRO try/catch: una banda non deve mai rompere una partita. Nessun `Kill`, nessun
    /// `Damage`, nessun `Death`, nessun `SetHealth`, nessun `Time.timeScale`, nessuna reflection sui
    /// campi privati, nessuna rete, nessun bersaglio Harmony in piu'.
    /// </summary>
    internal static class LifeGuiltBand
    {
        /// <summary>Il marker: lo stesso prefisso della colpa sulla vita, cosi' una ricerca sola li trova tutti.</summary>
        internal const string MARK = "[ColpaVita]";

        /// <summary>
        /// Il nome del GameObject della banda. Serve anche a **non duplicarla**: prima di crearne una si
        /// cerca un figlio con questo nome (vedi `CreateBand`).
        /// </summary>
        private const string BAND_NAME = "ColpaVita_Band";

        /// <summary>
        /// IL COLORE PROPOSTO: sangue scuro, semitrasparente. Si vede il riempimento sotto, quindi la
        /// banda non nasconde la vita — la **riduce**.
        /// </summary>
        private static readonly Color BandColor = new Color(0.42f, 0.05f, 0.07f, 0.62f);

        /// <summary>
        /// Ogni quanto il tick guarda lo stato, se nessuno l'ha forzato. Un quarto di secondo e' molto
        /// piu' corto di qualunque gesto umano e lunghissimo per un frame: il tick costa due letture.
        /// </summary>
        private const float TICK_INTERVAL = 0.25f;

        /// <summary>Tolleranza sul confronto con `Life.Final`: 1%, con un minimo di mezzo pixel.</summary>
        private const float WIDTH_TOLERANCE_MIN = 0.5f;

        // ---- la cache. La UI non si ricrea: trovata una volta, resta. Se un oggetto viene distrutto
        //      (Unity lo fa diventare `== null`) si ricostruisce da solo al giro dopo.
        private static PlayerHealth _widget;
        private static RectTransform _bar;
        private static Image _barImage;
        private static RectTransform _parent;
        private static Image _band;

        // ---- cosa e' disegnato adesso, per non riscrivere (e non loggare) due volte lo stesso stato.
        private static float _shownFactor = -1f;
        private static float _shownBarWidth = -1f;
        private static bool _shownSideRight = true;

        /// <summary>Il throttle del tick e il backoff quando la banda non si puo' ancora disegnare.</summary>
        private static float _nextCheck;

        /// <summary>Per non ripetere il "non trovata" a ogni tick: si logga al primo, poi si tace.</summary>
        private static bool _missingLogged;

        /// <summary>`GetWorldCorners` vuole un array: uno solo, riusato (niente allocazioni per frame).</summary>
        private static readonly Vector3[] _corners = new Vector3[4];

        // ============================================================================================
        // I DUE PUNTI D'INGRESSO: il tick (per frame, fuori da ogni widget) e il refresh forzato
        // (spawn / morte / falo').
        // ============================================================================================

        /// <summary>
        /// Il **tick leggero** della mod. Chiamato da `Multiplayer.OnLateUpdate`, cioe' dal ciclo per
        /// frame che la mod ha gia': gira anche quando `PlayerHealth` e `PlayerFervour` sono disabilitati
        /// dal gioco (la trappola della sonda, §4) e anche a `timeScale = 0` (`LateUpdate` non e' scalato
        /// dal tempo di gioco). Non crea nessun bersaglio Harmony.
        ///
        /// Costo per frame: un confronto sul throttle e, ogni `TICK_INTERVAL`, due letture
        /// (`Life.Final` e `Life.MaxFactor`) piu' due confronti di float. Quando nulla cambia non scrive
        /// niente: ne' sullo schermo, ne' nel log.
        /// </summary>
        internal static void Tick()
        {
            try
            {
                float now = Time.realtimeSinceStartup;
                if (now < _nextCheck)
                    return;

                _nextCheck = now + TICK_INTERVAL;
                Step("tick", false);
            }
            catch (Exception e)
            {
                Fail("tick", e);
            }
        }

        /// <summary>
        /// Un refresh **immediato e forzato**: non aspetta il tick e stampa una riga anche se il fattore
        /// non e' cambiato, cosi' il log dice cosa e' successo all'evento che lo ha chiesto.
        ///
        /// I tre chiamanti sono gli stessi tre eventi della colpa: lo **spawn**
        /// (`SpawnManager.CreatePlayer`, dove `Life.MaxFactor` viene riapplicato), la **morte**
        /// (`Penitent.OnUpdate`, dove il contatore sale) e il **falo'** (l'unico azzeramento).
        /// </summary>
        internal static void Refresh(string why)
        {
            try
            {
                _nextCheck = 0f;
                Step(why, true);
            }
            catch (Exception e)
            {
                Fail("refresh (" + why + ")", e);
            }
        }

        // ============================================================================================
        // IL PASSO — legge lo stato, poi decide: nascondi, disegna, o non fare niente.
        // ============================================================================================

        /// <summary>
        /// Lo stato che conta e' **quello applicato**, non quello voluto: `Life.MaxFactor` e `Life.Final`
        /// letti dalle statistiche del `Penitent` in scena. Cosi' la banda non puo' raccontare una
        /// penalita' che il gioco non sta applicando (o il contrario).
        /// </summary>
        private static void Step(string why, bool force)
        {
            float factor;
            float final;

            if (!ReadState(out factor, out final))
            {
                // Nessun Penitent (menu, caricamento, nessuna partita): non c'e' niente da rappresentare.
                // Si spegne in silenzio — un log qui sarebbe una riga per frame nel menu'.
                HideQuietly();
                return;
            }

            if (factor >= 0.999f)
            {
                HideBand(factor, why, force);
                return;
            }

            DrawBand(factor, final, why, force);
        }

        /// <summary>Fattore e larghezza della barra, dalla sola fonte pubblica che li ha: `Stats`.</summary>
        private static bool ReadState(out float factor, out float final)
        {
            factor = 1f;
            final = 0f;

            try
            {
                if (Core.Logic == null || Core.Logic.Penitent == null)
                    return false;

                if (Core.Logic.Penitent.Stats == null || Core.Logic.Penitent.Stats.Life == null)
                    return false;

                final = Core.Logic.Penitent.Stats.Life.Final;
                factor = Core.Logic.Penitent.Stats.Life.MaxFactor;

                // I tre scalini sono 1.0 / 0.8 / 0.6 / 0.4: qualunque altro numero e' fuori vocabolario e
                // va riportato dentro, altrimenti la banda uscirebbe dalla barra.
                if (factor < 0f)
                    factor = 0f;
                if (factor > 1f)
                    factor = 1f;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ============================================================================================
        // LA GEOMETRIA — la banda occupa (1 - fattore) x larghezza della barra, dal lato vuoto.
        // ============================================================================================

        /// <summary>Disegna (o aggiorna) la banda. Non crea un secondo esemplare: vedi `CreateBand`.</summary>
        private static void DrawBand(float factor, float final, string why, bool force)
        {
            if (!EnsureBar(final))
                return;

            try
            {
                if (_band == null)
                {
                    _band = CreateBand();
                    _shownFactor = -1f;
                    _shownBarWidth = -1f;
                }

                if (_band == null || _parent == null || _bar == null)
                    return;

                // ---- i due angoli della barra, nello spazio del PADRE: nessuna assunzione su ancore,
                //      pivot o scala del prefab.
                _bar.GetWorldCorners(_corners);
                Vector3 bottomLeft = _parent.InverseTransformPoint(_corners[0]);
                Vector3 topRight = _parent.InverseTransformPoint(_corners[2]);

                float barWidth = topRight.x - bottomLeft.x;
                float barHeight = topRight.y - bottomLeft.y;

                if (barWidth <= 0f || barHeight <= 0f)
                {
                    Missing("la barra ha larghezza/altezza nulla (" + F1(barWidth) + " x " + F1(barHeight) + ")");
                    return;
                }

                // ⚠️ La tolleranza del criterio si riapplica QUI sulla misura vera: se la barra non e'
                //     larga `Life.Final`, il "rapporto" non e' quello della penalita' e la banda
                //     rischierebbe di coprire il pezzo sbagliato. Meglio nessuna banda.
                float tolerance = Mathf.Max(WIDTH_TOLERANCE_MIN, Mathf.Abs(final) * 0.01f);
                if (Mathf.Abs(barWidth - final) > tolerance)
                {
                    // La banda gia' disegnata non e' piu' la rappresentazione giusta: la si spegne
                    // subito, perche' una banda **storta** e' peggio di nessuna banda.
                    if (_band != null && _band.enabled)
                        _band.enabled = false;

                    Missing("la barra trovata e' larga " + F1(barWidth) + " ma Life.Final vale " + F1(final) +
                            " (differenza oltre la tolleranza " + F1(tolerance) + ")");
                    return;
                }

                // ---- IL LATO. Il vincolo del mandato e' il **destro**; ma se il riempimento parte da
                //      destra (`fillOrigin == Right`) il pezzo vuoto e' a **sinistra**, e la banda deve
                //      cadere li': rappresenta il vuoto, non un lato. Caso mai visto: si dichiara nel log.
                bool onRight = !FillsFromRight();
                float bandWidth = barWidth * (1f - factor);

                if (bandWidth < 0f)
                    bandWidth = 0f;

                RectTransform bandRect = _band.rectTransform;
                Rect bandParentRect = _parent.rect;

                bandRect.anchorMin = Vector2.zero;
                bandRect.anchorMax = Vector2.zero;
                bandRect.pivot = Vector2.zero;
                bandRect.sizeDelta = new Vector2(bandWidth, barHeight);

                // Ancore a (0,0) con pivot (0,0): `anchoredPosition` e' l'angolo in basso a sinistra
                // della banda rispetto all'angolo in basso a sinistra del padre.
                bandRect.anchoredPosition = new Vector2(
                    (onRight ? topRight.x - bandWidth : bottomLeft.x) - bandParentRect.xMin,
                    bottomLeft.y - bandParentRect.yMin);

                bandRect.localScale = Vector3.one;
                bandRect.localRotation = Quaternion.identity;

                // Sopra la barra: ultima sorella del padre (cioe' sopra `health`, `loss` e lo sfondo).
                if (_band.transform.GetSiblingIndex() != _parent.childCount - 1)
                    _band.transform.SetAsLastSibling();

                bool wasHidden = !_band.enabled;
                if (wasHidden)
                    _band.enabled = true;

                bool changed = wasHidden
                               || Mathf.Abs(_shownFactor - factor) > 0.0005f
                               || Mathf.Abs(_shownBarWidth - barWidth) > 0.01f
                               || _shownSideRight != onRight;

                if (changed || force)
                {
                    ModLog.Info(MARK + " banda: fattore=" + Text(factor) +
                                " larghezza=" + F1(bandWidth) +
                                " ancoraggio=" + (onRight ? "destra" : "sinistra") +
                                " (padre della barra, ultima sorella) · barra: larghezza=" + F1(barWidth) +
                                " altezza=" + F1(barHeight) + " final=" + F1(final) +
                                " · colore=rgba(0.42,0.05,0.07,0.62) · perche'=" + why);
                }

                _shownFactor = factor;
                _shownBarWidth = barWidth;
                _shownSideRight = onRight;
            }
            catch (Exception e)
            {
                Fail("banda (" + why + ")", e);
            }
        }

        /// <summary>
        /// Nasconde la banda. `Image.enabled = false` e non `Destroy`: la UI non si ricrea, quindi
        /// distruggerla e ricrearla a ogni falo' sarebbe lavoro e un rischio di doppioni. Spenta non
        /// disegna **niente**: nessun residuo visivo.
        /// </summary>
        private static void HideBand(float factor, string why, bool force)
        {
            try
            {
                bool wasVisible = _band != null && _band.enabled;

                if (_band != null)
                    _band.enabled = false;

                if (wasVisible || force)
                {
                    ModLog.Info(MARK + " banda nascosta · fattore=" + Text(factor) +
                                " · fattore 1.0 = nessuna colpa sulla vita, nessun residuo visivo · perche'=" + why);
                }

                _shownFactor = factor;
                _shownBarWidth = -1f;
            }
            catch (Exception e)
            {
                Fail("nascondi (" + why + ")", e);
            }
        }

        /// <summary>Nessun `Penitent`: si spegne senza log (e' il menu', il caricamento, la schermata di avvio).</summary>
        private static void HideQuietly()
        {
            if (_band != null && _band.enabled)
                _band.enabled = false;

            _shownFactor = -1f;
            _shownBarWidth = -1f;
        }

        // ============================================================================================
        // L'AGGANCIO — `PlayerHealth.transform` + il criterio pubblico `Filled` + larghezza ≈ `Final`.
        // ============================================================================================

        /// <summary>
        /// Trova (una volta) la barra e il padre su cui disegnare. Il `PlayerHealth` si cerca con
        /// `FindObjectOfType`, che e' una **scansione di scena**: la si fa solo quando la cache e' vuota,
        /// e il `Tick` la protegge con il throttle — lo stesso accorgimento di `PauseNotice` (`:1121`,
        /// `:1131`) per la stessa ragione.
        ///
        /// ⚠️ `FindObjectOfType` **non guarda gli oggetti disattivati** (`MapManager.cs:126-127`), ma qui
        /// non e' un problema: il gioco disabilita il **componente** (`PlayerHealth.Awake` fa
        /// `enabled = false`, `:66`), non il GameObject della HUD. Il riferimento in cache resta valido, e
        /// quando la HUD e' nascosta si nasconde con lei anche la banda (e' un suo figlio).
        /// </summary>
        private static bool EnsureBar(float final)
        {
            if (_widget != null && _bar != null && _parent != null)
                return true;

            try
            {
                if (_widget == null)
                    _widget = UnityEngine.Object.FindObjectOfType<PlayerHealth>();

                if (_widget == null)
                {
                    Missing("PlayerHealth non in scena: la HUD non e' ancora caricata (la banda arrivera' al primo tick utile)");
                    return false;
                }

                if (final <= 0.01f)
                {
                    Missing("Life.Final=" + F1(final) + ": una barra di larghezza zero non ha un lato da coprire");
                    return false;
                }

                _bar = FindBar(_widget, final);
                _barImage = _bar == null ? null : _bar.GetComponent<Image>();
                _parent = _bar == null ? null : _bar.parent as RectTransform;
                _band = null;

                if (_bar == null || _parent == null)
                {
                    Missing("nessuna Image con type=Filled e larghezza " + F1(final) +
                            " sotto PlayerHealth (\"" + (_widget == null ? "?" : _widget.name) + "\")");
                    return false;
                }

                if (_missingLogged)
                {
                    ModLog.Info(MARK + " barra TROVATA · \"" + _bar.name + "\" · type=Filled · larghezza=" +
                                F1(_bar.rect.width) + " final=" + F1(final) +
                                " · padre=\"" + _parent.name + "\" · da qui in poi la banda si aggiorna soltanto");
                }

                _missingLogged = false;
                return true;
            }
            catch (Exception e)
            {
                Fail("aggancio alla barra", e);
                return false;
            }
        }

        /// <summary>
        /// Il criterio, per esteso: **`Image` con `type == Filled` e larghezza ≈ `Life.Final`**.
        ///
        /// La larghezza si accetta su **due misure**, e non e' indulgenza: `rect.width` e' cio' che si
        /// vede (la sonda lo indica come criterio), `sizeDelta.x` e' cio' che il gioco **scrive**
        /// (`PlayerHealth.cs:123-125`). Se le ancore della barra fossero stirate le due divergerebbero, e
        /// accettarle entrambe rende il criterio **piu' stretto**, non piu' largo: il numero deve essere
        /// comunque `Life.Final`, su una delle due. Fra i candidati si prende quello con l'errore minore.
        /// </summary>
        private static RectTransform FindBar(PlayerHealth widget, float final)
        {
            Image[] images = widget.GetComponentsInChildren<Image>(true);
            if (images == null || images.Length == 0)
                return null;

            float tolerance = Mathf.Max(WIDTH_TOLERANCE_MIN, Mathf.Abs(final) * 0.01f);
            RectTransform best = null;
            float bestError = float.MaxValue;

            foreach (Image image in images)
            {
                try
                {
                    if (image == null)
                        continue;

                    if (image.type != Image.Type.Filled)
                        continue;

                    RectTransform rect = image.rectTransform;
                    if (rect == null)
                        continue;

                    // La banda e' `Simple`: questa riga e' una cintura in piu' contro l'auto-aggancio.
                    if (rect.gameObject != null && rect.gameObject.name == BAND_NAME)
                        continue;

                    float error = Mathf.Min(Mathf.Abs(rect.rect.width - final), Mathf.Abs(rect.sizeDelta.x - final));
                    if (error > tolerance)
                        continue;

                    if (error < bestError)
                    {
                        bestError = error;
                        best = rect;
                    }
                }
                catch (Exception)
                {
                    // Una Image strana non deve fermare la ricerca delle altre.
                }
            }

            return best;
        }

        /// <summary>
        /// Vero se il riempimento della barra parte da **destra** (`Image.fillOrigin == Right` con
        /// `fillMethod == Horizontal`): in quel caso il vuoto e' a sinistra e la banda va li'.
        /// Se non si puo' leggere, si resta sul **destro**, che e' il lato chiesto.
        /// </summary>
        private static bool FillsFromRight()
        {
            try
            {
                if (_barImage == null)
                    return false;

                if (_barImage.type != Image.Type.Filled)
                    return false;

                if (_barImage.fillMethod != Image.FillMethod.Horizontal)
                    return false;

                return _barImage.fillOrigin == (int)Image.OriginHorizontal.Right;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ============================================================================================
        // LA CREAZIONE — una volta sola, e mai un doppione.
        // ============================================================================================

        /// <summary>
        /// Crea la banda: un GameObject con `RectTransform` + `Image`, figlio del padre della barra.
        ///
        /// ⚠️ Si crea **inattiva** e si attiva solo alla fine: una `Image` configurata a meta' (ancore a
        /// zero, colore di default) farebbe un lampo bianco di un frame.
        ///
        /// ⚠️ E **prima di crearla si cerca un figlio con lo stesso nome**: se per qualunque ragione una
        /// banda fosse gia' li' (un frame in cui la cache era vuota e il padre gia' esisteva), si riusa
        /// quella. E' la difesa contro la "banda doppia dopo un respawn", che e' il caso n.2 dei casi che
        /// potrebbero peggiorare.
        /// </summary>
        private static Image CreateBand()
        {
            try
            {
                if (_parent == null)
                    return null;

                // 1. riuso, se c'e' gia' (nessun doppione, mai).
                Transform existing = _parent.Find(BAND_NAME);
                if (existing != null)
                {
                    Image reused = existing.GetComponent<Image>();
                    if (reused != null)
                    {
                        reused.color = BandColor;
                        reused.raycastTarget = false;
                        return reused;
                    }

                    // Un figlio con quel nome ma senza Image e' un residuo inutile: via.
                    UnityEngine.Object.Destroy(existing.gameObject);
                }

                // 2. creazione. `Image` con `sprite = null` disegna un rettangolo pieno del colore
                //    indicato: e' il comportamento documentato di `Graphic.OnPopulateMesh`, ed e' lo
                //    stesso espediente che la mod usa gia' per lo sfondo delle notifiche
                //    (`NotificationManager.cs:192-200`).
                GameObject obj = new GameObject(BAND_NAME, typeof(RectTransform), typeof(Image));
                obj.SetActive(false);
                obj.transform.SetParent(_parent, false);

                Image image = obj.GetComponent<Image>();
                if (image == null)
                {
                    UnityEngine.Object.Destroy(obj);
                    Missing("il GameObject della banda non ha una Image: niente banda");
                    return null;
                }

                image.sprite = null;
                image.type = Image.Type.Simple;
                image.color = BandColor;
                image.raycastTarget = false;
                image.enabled = true;

                // Un layout group sul padre sposterebbe la banda: questa riga glielo vieta.
                LayoutElement layout = obj.GetComponent<LayoutElement>();
                if (layout == null)
                    layout = obj.AddComponent<LayoutElement>();
                if (layout != null)
                    layout.ignoreLayout = true;

                // Sopra la barra (e sopra la scia del danno): ultima sorella del padre.
                obj.transform.SetAsLastSibling();

                obj.SetActive(true);

                ModLog.Info(MARK + " banda creata · \"" + BAND_NAME + "\" sotto \"" + _parent.name +
                            "\" · una volta sola: la UI non si ricarica, da qui in poi si aggiorna");

                return image;
            }
            catch (Exception e)
            {
                Fail("creazione della banda", e);
                return null;
            }
        }

        // ============================================================================================
        // I PEZZI PICCOLI.
        // ============================================================================================

        /// <summary>Riporta la prima volta che la banda non si puo' disegnare, poi tace (un tick non e' un log).</summary>
        private static void Missing(string why)
        {
            if (_missingLogged)
                return;

            _missingLogged = true;
            ModLog.Warn(MARK + " banda NON disegnata · " + why +
                        " · meglio nessuna banda che una banda nel posto sbagliato: si riprova al prossimo tick");
        }

        /// <summary>Il fattore in forma stabile (`0.8`, non `0,8`): i log si cercano.</summary>
        private static string Text(float factor)
        {
            return factor.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>Un decimale con il punto: il log e' una misura, non un testo locale.</summary>
        private static string F1(float value)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>Una riga di errore che non puo' a sua volta lanciare (altrimenti lancerebbe dentro un catch).</summary>
        private static void Fail(string what, Exception e)
        {
            try
            {
                ModLog.Error(MARK + " banda: " + what + " fallita (" + e.GetType().Name + ": " + e.Message + ")" +
                             " · la partita continua: al massimo manca una banda");
            }
            catch (Exception)
            {
                // Una banda che esplode mentre riporta di essere esplosa non deve fermare niente.
            }
        }
    }
}
