using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Client.Players;
using Framework.Managers;
using Framework.Map;
using Gameplay.UI.Others.MenuLogic;
using UnityEngine;
using UnityEngine.UI;

namespace Blasphemous.Multiplayer.Client.Map
{
    public class MapManager
    {
        // Temporarily holds the most recent player map position
        public Vector2 ActivePlayerPosition { get; set; }

        // A map update is queued whenever player changes team, enters/leaves a scene, etc.
        private bool mapUpdateQueued;

        // ⭐ C — "la chiamata e' stata saltata" si stampa UNA volta per richiesta, non a ogni frame.
        // Senza questo flag il marker diventerebbe una raffica di 60 righe al secondo: la stessa forma
        // che il difetto aveva nel log delle eccezioni, solo con un'altra etichetta.
        private bool mapUpdateSkipLogged;

        // ⭐ C — il widget della mappa, tenuto da parte, e l'orologio della ricerca.
        // `FindObjectOfType` e' una SCANSIONE DI SCENA e non va fatta a ogni frame: si fa al massimo
        // ogni MAP_LOOKUP_INTERVAL secondi NON SCALATI (in pausa `Time.time` e' fermo, `Time.unscaledTime`
        // no: e' l'unico orologio che serve qui, come in `BossDeathWarning.cs:61-65`).
        private NewMapMenuWidget mapWidget;
        private float nextMapLookupAt;
        private const float MAP_LOOKUP_INTERVAL = 0.25f;

        // UI created after first time opening the map
        private Transform playerMarks;
        private Sprite[] playerSprites;

        public void QueueMapUpdate()
        {
            mapUpdateQueued = true;
        }

        // ============================================================================================
        // ⭐⭐ C — IL FIX DELLA NULLREFERENCE (le 63 di `Map.MapManager.RefreshMap` ← `Multiplayer.OnLateUpdate`).
        // Diagnosi completa: `scratch\DIAGNOSI-63-NRE.md`. Referto: `scratch\REFERTO-NRE-E-PAUSA.md`.
        //
        // LA CATENA, in cinque passi. `Multiplayer.OnLateUpdate` chiama questo `Update` ogni frame; qui,
        // se c'e' una richiesta in coda, si chiama `RefreshMap(false)`; `RefreshMap` crea la UI **solo se
        // `playerMarks == null`**; `CreateMapUI` **esce senza fare niente** se il widget della mappa non
        // e' attivo; e `RefreshMap` **prosegue lo stesso** fino a `playerMarks.childCount` ⇒ NRE.
        // Il difetto non e' la pausa: e' che **la guardia e' PRIMA del tentativo** invece che DOPO.
        //
        // PERCHE' LA PAUSA C'ENTRA, E VA DETTO BENE: **la mappa si apre DALLA pausa** — e' una scheda
        // del menu di pausa (`PauseWidget.MapWidget`, `canvasMap = MapWidget.GetComponent<CanvasGroup>()`,
        // `PauseWidget.cs:53`; la scheda mostrata la decide `SetChildActive` → `SetActive`, `:157-161`,
        // `:199`). Le ALTRE vie di pausa (inventario, tutorial, popup di sblocco) alzano `Core.Logic.IsPaused`
        // **senza mostrare la mappa** ⇒ finche' la scheda mappa non e' stata mostrata **nemmeno una volta**,
        // **ogni** pausa di un'altra via puo' far cadere qui un evento del peer, e da li' la raffica:
        // `mapUpdateQueued` non si azzerava (l'eccezione usciva prima) ⇒ **una eccezione per frame**.
        // ⚠️ `0 giocatori connessi` NON e' una difesa (la `:59` viene PRIMA del ciclo sui giocatori):
        //    la difesa e' che **`mapUpdateQueued` non si accende senza un peer**, perche' i suoi tre soli
        //    scrittori sono tutti eventi del peer (`OtherPlayerManager.cs:501`, `:513`, `Multiplayer.cs:390`).
        //
        // LE TRE MODIFICHE, e ognuna toglie un pezzo della catena:
        //   ① `CreateMapUI` **dice se ce l'ha fatta** (`bool`) e `RefreshMap` **esce prima della `:59`**.
        //   ③ il predicato diventa **"la scheda mappa e' quella MOSTRATA"** (`IsMapOnScreen()`), non
        //      "il gioco e' in pausa": con questo, il caso "in pausa sull'inventario" **non entra nemmeno**
        //      in `RefreshMap`.
        //   ② `mapUpdateQueued` si azzera **PRIMA** della chiamata — ma **servita, non buttata**: se la
        //      mappa non e' a schermo la richiesta **resta in coda** e verra' servita quando lo sara'.
        //      (Il difetto vicino che si chiude qui: prima il flag veniva azzerato in OGNI caso, quindi un
        //      evento che cadeva su un frame non in pausa veniva **buttato via** in silenzio.)
        //
        // COSA PERDIAMO: **solo** l'icona che si aggiorna **a mappa gia' aperta**. L'apertura e' coperta
        // dal patch `OnShow` → `RefreshMap(true)` (`MapPatches.cs:11-17`), che e' l'unico chiamante con
        // `forceRecalculate`. Nessuna riga del gioco e' toccata: zero patch Harmony nuove.
        // ============================================================================================
        public void Update()
        {
            if (!mapUpdateQueued)
            {
                mapUpdateSkipLogged = false;
                return;
            }

            // ⭐ ③ LA DECISIONE — "la scheda mappa e' quella mostrata", non "il gioco e' in pausa".
            if (!IsMapOnScreen())
            {
                // La richiesta NON si butta: resta in coda. Il marker dice il PERCHE', una volta sola.
                if (!mapUpdateSkipLogged)
                {
                    mapUpdateSkipLogged = true;
                    ModLog.Info("[Map] richiesta di ridisegno IN CODA e chiamata SALTATA: la scheda mappa NON e' " +
                                "a schermo (quindi `CreateMapUI` non creerebbe niente e `playerMarks` sarebbe null) · " +
                                "motivo=" + PauseCause() + " · mappaAschermo=false · la richiesta RESTA in coda " +
                                "(servita, non buttata): verra' servita quando la mappa sara' aperta");
                }
                return;
            }

            // ⭐ ② SERVITA, NON BUTTATA — e azzerata PRIMA della chiamata: se `RefreshMap` lanciasse
            // comunque, l'evento costa UNA riga invece di una raffica da 60 al secondo.
            mapUpdateQueued = false;
            mapUpdateSkipLogged = false;

            try
            {
                RefreshMap(false);
            }
            catch (System.Exception e)
            {
                // Difesa di secondo livello: le ①③ dovrebbero rendere questo ramo irraggiungibile.
                // Non nasconde niente — lo dice — e non deve MAI uscire dentro `OnLateUpdate`.
                ModLog.Error("[Map] RefreshMap ha lanciato " + e.GetType().Name + ": " + e.Message +
                             " — l'icona dell'altro sulla mappa resta quella di prima, il resto della mod no " +
                             "(questo catch non lancia)");
            }
        }

        // ============================================================================================
        // ⭐ ③ IL PREDICATO GIUSTO: **la scheda mappa e' quella MOSTRATA**.
        //
        // PERCHE' NON `Core.Logic.IsPaused`. `IsPaused` lo alzano CINQUE UI (`BasicUIBlockingWidget.cs:41`,
        // `NewInventoryWidget.cs:197`, `:231`, `TutorialManager.cs:110`, `UIController.cs:651`,
        // `MapMenuWidget.cs:623`, `:641`) e **la mappa e' una sola**: "in pausa" e' un SOPRAINSIEME della
        // domanda che serve. Da quella differenza di una parola nasceva tutto il difetto.
        //
        // PERCHE' `activeInHierarchy` E' LA PROVA, e non una deduzione: `Object.FindObjectOfType<T>()`
        // **non guarda gli oggetti disattivati**, quindi `FindObjectOfType<NewMapMenuWidget>() != null`
        // significa esattamente "quel GameObject e' attivo" — ed e' lo stesso test che fa `CreateMapUI`
        // prima di rinunciare. Qui la stessa domanda si fa su un riferimento tenuto da parte, perche'
        // ripetere la scansione di scena a ogni frame sarebbe un costo che non serve a nessuno.
        // (Equivalenti: `isActiveAndEnabled` chiederebbe in piu' che il componente sia `enabled`; il
        // discriminante vero e' quello che il gioco scrive, cioe' `canvasMap.gameObject.SetActive(value)`,
        // `PauseWidget.cs:157-161` e `:199`.)
        // ============================================================================================
        private bool IsMapOnScreen()
        {
            if (mapWidget != null && mapWidget.gameObject.activeInHierarchy)
                return true;

            // Nessun riferimento (o non piu' attivo): si cerca, ma non a ogni frame.
            if (Time.unscaledTime < nextMapLookupAt)
                return false;

            nextMapLookupAt = Time.unscaledTime + MAP_LOOKUP_INTERVAL;
            mapWidget = Object.FindObjectOfType<NewMapMenuWidget>();
            return mapWidget != null && mapWidget.gameObject.activeInHierarchy;
        }

        // ============================================================================================
        // IL MOTIVO, per il marker. Se il gioco e' in pausa e la mappa non e' a schermo, **quale** delle
        // vie e' aperta: i nomi sono quelli che il gioco mette nei blocker d'input
        // (`BasicUIBlockingWidget.cs:39` UIBLOCKING, `NewInventoryWidget.cs:166` INVENTORY,
        // `TutorialManager.cs:109` TUTORIAL, `UIController.cs:650` UNLOCK, `MapMenuWidget.cs:620`
        // INGAME_MENU) e si leggono con `Core.Input.HasBlocker`, API PUBBLICA del gioco e gia' usata
        // dalla mod (`PvP\AttackManager.cs:41`, `CutsceneProbe.cs:106`).
        // ⚠️ Sono CINQUE stringhe, e sono SOLO un marker: se il gioco ne aggiungesse una sesta, il marker
        // direbbe `pausa-senza-mappa`. **La decisione non legge nessun nome**: e' `IsMapOnScreen()`.
        // ============================================================================================
        private static string PauseCause()
        {
            try
            {
                if (!Core.Logic.IsPaused)
                    return "gioco-non-in-pausa";

                if (Core.Input.HasBlocker("INVENTORY")) return "inventario-a-schermo";
                if (Core.Input.HasBlocker("TUTORIAL")) return "tutorial-a-schermo";
                if (Core.Input.HasBlocker("UNLOCK")) return "sblocco-a-schermo";
                if (Core.Input.HasBlocker("INGAME_MENU")) return "mappa-in-game-senza-scheda";
                if (Core.Input.HasBlocker("UIBLOCKING")) return "menu-di-pausa-su-un-altra-scheda";

                return "pausa-senza-mappa";
            }
            catch (System.Exception)
            {
                return "? (lettura dei blocker fallita)";
            }
        }

        public void RefreshMap(bool forceRecalculate)
        {
            // Only add marks for other players if config enabled
            if (!Main.Multiplayer.config.showPlayersOnMap)
                return;
            ModLog.Info("Updating map with new player marks!");

            // Destroy old holder to put players on top of other marks
            if (forceRecalculate && playerMarks != null)
            {
                Object.Destroy(playerMarks.gameObject);
                playerMarks = null;
            }

            // If holder doesn't exist yet, create it
            if (playerMarks == null || playerSprites == null)
            {
                // ⭐ ① LA GUARDIA DOPO IL TENTATIVO, che e' l'unica che dice la verita'.
                // Prima qui si chiamava `CreateMapUI()` (che e' `void` e puo' uscire senza creare
                // niente) e si proseguiva lo stesso: alla riga `playerMarks.childCount` ⇒ NRE.
                if (!CreateMapUI())
                {
                    ModLog.Info("[Map] CreateMapUI non ha creato niente (scheda mappa non a schermo): " +
                                "esco PRIMA di leggere `playerMarks` — la NRE non c'e' piu' · motivo=" +
                                PauseCause() + " · mappaAschermo=false");
                    return;
                }
            }

            // Destroy all old player marks
            for (int i = playerMarks.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(playerMarks.GetChild(i).gameObject);
            }

            // Create a new mark for each player
            foreach (PlayerStatus player in Main.Multiplayer.OtherPlayerManager.AllConnectedPlayers)
            {
                // Only show other teams if config option
                if (player.Team != Main.Multiplayer.PlayerTeam && !Main.Multiplayer.config.showOtherTeamOnMap)
                    continue;

                // Calling this function with -1000 will calculate the center position of the scene
                Core.NewMapManager.GetCellKeyFromPosition(player.LastMapScene, new Vector2(-1000, 0));
                Vector2 cellPosition = ActivePlayerPosition;
                if (cellPosition.x < 0 || cellPosition.y < 0)
                    continue;

                // Calculate which icon to use
                Sprite icon = playerSprites[0];
                if (player.CurrentScene != player.LastMapScene)
                {
                    icon = playerSprites[1];
                }
                else if (player.Team != Main.Multiplayer.PlayerTeam)
                {
                    icon = playerSprites[2];
                }

                // Create new image for this player
                GameObject obj = new GameObject(player.Name, typeof(RectTransform));
                RectTransform rect = obj.GetComponent<RectTransform>();
                rect.SetParent(playerMarks, false);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                rect.localPosition = new Vector2(16 * cellPosition.x, 16 * cellPosition.y);
                rect.sizeDelta = new Vector2(playerSprites[0].rect.width, playerSprites[0].rect.height);
                rect.gameObject.AddComponent<Image>().sprite = icon;
                ModLog.Info($"Creating mark at " + rect.localPosition);
            }
        }

        // ============================================================================================
        // ⭐ ① — **DICE SE CE L'HA FATTA**. Era `void` e i due fallimenti erano due `return` muti: il
        // chiamante non aveva modo di sapere che `playerMarks` era rimasto `null`. Ora il valore di ritorno
        // e' l'unica cosa che `RefreshMap` deve guardare prima di usare l'holder.
        // ============================================================================================
        private bool CreateMapUI()
        {
            // Find Map widget
            NewMapMenuWidget widget = Object.FindObjectOfType<NewMapMenuWidget>();
            if (widget == null)
                return false;
            Transform rootRenderer = widget.transform.Find("Background/Map/MapMask/MapRoot/RootRenderer_0");
            if (rootRenderer == null)
                return false;

            // Il widget trovato e' quello giusto: si tiene da parte per `IsMapOnScreen()`.
            if (mapWidget == null)
                mapWidget = widget;

            // Create child of renderer
            playerMarks = new GameObject("PlayerMarks", typeof(RectTransform)).transform;
            playerMarks.SetParent(rootRenderer, false);

            // Get player images
            MapRendererConfig cfg = widget.RendererConfigs[0];
            playerSprites = new Sprite[3] { cfg.Marks[MapData.MarkType.Blue], cfg.Marks[MapData.MarkType.Green], cfg.Marks[MapData.MarkType.Red] };

            return true;
        }
    }
}
