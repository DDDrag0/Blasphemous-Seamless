using Blasphemous.ModdingAPI;
using Blasphemous.Multiplayer.Common;
using CoopServer = Blasphemous.Multiplayer.Server.Server;
using System;
using System.Net;
using System.Net.Sockets;

namespace Blasphemous.Multiplayer.Client.Host
{
    /// <summary>
    /// ⭐ LA SESSIONE-HOST: IL SERVER DENTRO IL CLIENT.
    ///
    /// E' il pezzo che mancava per avere UNA sola applicazione: prima per ospitare bisognava lanciare
    /// `SERVER-COOP\blas1mpserver.exe` (un processo a parte, una finestra console, un `esc` per
    /// uscire), e chi giocava doveva sapere l'IP di quella macchina. Adesso lo stesso gioco apre la
    /// porta con lo stesso engine (`Blasphemous.Multiplayer.Server.Server`, compilato dentro questa
    /// DLL da `Blasphemous.Multiplayer.Client.csproj`) e si connette a se stesso su `127.0.0.1`.
    ///
    /// COSA FA, IN TRE RIGHE:
    ///   1. controlla che la porta sia LIBERA (prova a legarla e la libera subito: serve solo a dare
    ///      un messaggio chiaro invece di un guasto a meta');
    ///   2. avvia l'engine (`Server.Start(port)`, che ritorna `bool` e non lancia);
    ///   3. lo ferma (`Server.Stop()`, che prima non esisteva: l'unico spegnimento era
    ///      `Environment.Exit(0)`).
    ///
    /// ⛔ QUESTO FILE NON CHIAMA MAI `Environment.Exit` NE `Console.*`, e non puo': nel processo del
    /// gioco `Environment.Exit(0)` chiude il gioco, e `Console.WriteLine`/`Console.ForegroundColor`
    /// lanciano `IOException` quando non c'e' una console (cioe' sempre). Le righe del server vanno
    /// nel log della mod: lo fa `Logger` quando e' compilato con `COOP_HOST`.
    ///
    /// ⚠️ LA CONVIVENZA CON IL SERVER ESTERNO, DICHIARATA. I due non si pestano i piedi perche' la
    /// porta e' una sola e questo file la PRETENDE libera: se `SERVER-COOP` (o un'altra sessione
    /// host, o qualunque cosa) sta gia' ascoltando su 27051, il bottone NON avvia niente e NON si
    /// connette da nessuna parte - scrive `[Host] porta occupata` e lo dice a schermo. In quel caso
    /// la strada giusta resta quella di sempre: premere "Connect" e usare quel server. Il contrario
    /// vale uguale: con l'host avviato qui, `SERVER-COOP` sulla stessa macchina non parte (porta
    /// occupata), mentre su un'ALTRA macchina non c'e' nessun conflitto.
    ///
    /// ⚠️ E IL FIREWALL: il `bind` su `0.0.0.0:27051` fa comparire la richiesta di autorizzazione di
    /// Windows la prima volta. Se la si nega, il server parte e il marker lo dice, ma dall'altra
    /// macchina non arriva niente. Non e' un guasto di questo codice e non c'e' modo di accorgersene
    /// da qui: e' scritto nel referto, nel test.
    /// </summary>
    internal static class HostSession
    {
        /// <summary>Il numero massimo di giocatori: lo stesso default dello standalone (`ServerArguments`).</summary>
        internal const int MAX_PLAYERS = 10;

        // ⭐ I MARKER DEL LOOPBACK — tutti NUOVI, tutti col prefisso `[Host]` di sempre: le righe
        // esistenti (`server avviato`, `indirizzo locale`, `porta occupata`, `server fermo`) restano
        // identiche, parola per parola. Servono a rispondere dal LOG, non dal codice, alle due domande
        // che il difetto dell'auto-connessione lasciava aperte:
        //   · su cosa e' legato il server (`ascolto su=`), cioe' se il loopback e' coperto dal bind;
        //   · quale indirizzo ha risposto davvero (`loopback pronto via=` / `connesso via=`): `ipv4`
        //     = `127.0.0.1`, `nome` = `localhost` (cioe' il resolver puo' aver scelto `::1`).
        internal const string MarkerBind = "[Host] ascolto su=";
        internal const string MarkerReady = "[Host] loopback pronto via=";
        internal const string MarkerConnected = "[Host] connesso via=";
        internal const string MarkerWaitFail = "[Host] errore: loopback";

        /// <summary>
        /// La riga del dial con **host e porta veri** e, in `OnConnect`, con l'**esito**:
        /// `[Host] dial locale host=… porta=… via=…` poi `[Host] dial locale esito=ok|rifiutato …`.
        /// Esiste perche' la riga storica `Attempting to connect to {indirizzo}` **non stampa la
        /// porta**: da sola non permetteva di escludere che il dial andasse su una porta diversa da
        /// quella in ascolto (uno dei due sospetti sul `Connection refused`), e adesso lo permette.
        /// </summary>
        internal const string MarkerDial = "[Host] dial locale ";

        /// <summary>`127.0.0.1`: l'indirizzo che non passa da nessuna risoluzione di nome.</summary>
        internal const string LOOPBACK_IPV4 = "127.0.0.1";

        /// <summary>`localhost`: il nome, che il resolver puo' mandare su `::1` (IPv6).</summary>
        internal const string LOOPBACK_NAME = "localhost";

        internal const string VIA_IPV4 = "ipv4";
        internal const string VIA_NOME = "nome";

        /// <summary>
        /// Il valore di `via=` quando la sonda e' SCADUTA e ci si connette lo stesso (best-effort): non
        /// e' una bugia, e' la sola cosa vera che si puo' scrivere - nessuno ha risposto in tempo.
        /// </summary>
        internal const string VIA_BESTEFFORT = "best-effort";

        private static CoopServer _server;

        /// <summary>Le istanze vive in QUESTO processo. Il marker la stampa: 1 se c'e' l'host, 0 se no.</summary>
        private static int _instances;

        /// <summary>
        /// L'ultimo motivo di un avvio fallito, in una riga leggibile: la schermata di connessione lo
        /// mostra nella notifica della mod (`"Host session: " + LastError`). Vuota se l'ultimo avvio
        /// e' riuscito. Il marker `[Host] ...` resta la traccia per il log: questo e' per il giocatore.
        /// </summary>
        internal static string LastError { get; private set; }

        /// <summary>`true` se l'host di questa macchina sta ascoltando adesso.</summary>
        internal static bool IsRunning
        {
            get { return _server != null; }
        }

        /// <summary>La porta dell'host, o 0 se non c'e' nessun host.</summary>
        internal static int Port
        {
            get { return _server != null ? _server.Port : 0; }
        }

        /// <summary>Quanti giocatori sono connessi all'host (0 se non c'e' host).</summary>
        internal static int Players
        {
            get
            {
                try
                {
                    if (_server == null)
                        return 0;

                    var players = _server.getPlayers();
                    return players == null ? 0 : players.Count;
                }
                catch (Exception)
                {
                    return 0;
                }
            }
        }

        /// <summary>
        /// Avvia l'host sulla porta indicata con la password della sessione (quella scritta nella
        /// schermata di connessione: e' la stessa che i peer dovranno usare, e la stessa che il
        /// client locale presenta connettendosi a `127.0.0.1`).
        ///
        /// Ritorna `false` se non e' partito, e in quel caso NON c'e' nessun server da fermare: chi
        /// chiama non deve fare pulizia, e non deve connettersi in locale (non c'e' niente che
        /// ascolti, e connettersi a `127.0.0.1` su una porta occupata da ALTRI vorrebbe dire entrare
        /// in una sessione che non e' la nostra).
        ///
        /// Non lancia: ogni guasto diventa un marker `[Host] ...` e un `false`.
        /// </summary>
        internal static bool TryStart(int port, string password)
        {
            try
            {
                LastError = string.Empty;

                if (port <= 0 || port > 65535)
                {
                    LastError = "porta non valida " + port;
                    ModLog.Error("[Host] errore: " + LastError);
                    return false;
                }

                if (_server != null)
                {
                    // Doppio clic sul bottone: non si avvia una seconda istanza sulla stessa porta.
                    ModLog.Info("[Host] server gia' avviato porta=" + _server.Port + " istanze=" + _instances);
                    return true;
                }

                if (Main.Multiplayer != null && Main.Multiplayer.NetworkManager != null &&
                    Main.Multiplayer.NetworkManager.IsConnected)
                {
                    // Gia' connessi a un server (esterno o nostro): ospitare adesso vorrebbe dire
                    // due sessioni sulla stessa macchina e nessuna delle due coerente.
                    LastError = "sei gia' connesso a un server: disconnettiti prima di ospitare";
                    ModLog.Warn("[Host] errore: " + LastError);
                    return false;
                }

                // ⭐⭐ LA PORTA, IN DUE PASSI, E IL SECONDO PASSO NON E' UN LUSSO.
                //
                // La sonda qui sotto e' un bind ESCLUSIVO: se qualunque socket tiene la porta, lei
                // fallisce - ed e' cio' che vogliamo per la convivenza con `SERVER-COOP`. Ma da sola
                // direbbe "occupata" anche quando non c'e' nessuno che ascolta, per due motivi veri:
                //   · una connessione in TIME_WAIT sulla porta (una sessione appena chiusa);
                //   · una NIC occupata e le altre libere (SimpleTCP lega per interfaccia e
                //     "ignora" quelle occupate: vedi il commento in `Server.Start`).
                // In quei due casi l'avvio vero RIUSCIREBBE, e dire "porta occupata" sarebbe una
                // bugia che l'utente non puo' spiegarsi.
                //
                // ⇒ Seconda opinione: c'e' qualcuno che RISPONDE su 127.0.0.1:porta? Se no, non c'e'
                //   un server da rispettare e si prova l'avvio vero. Se si', non si tocca niente:
                //   e' esattamente il caso "server esterno gia' attivo".
                if (!IsPortFree(port))
                {
                    if (SomeoneListens(port))
                    {
                        LastError = "la porta " + port + " e' gia' occupata (server esterno o altra sessione host)";
                        ModLog.Error("[Host] porta occupata");
                        ModLog.Error("[Host] dettaglio: qualcuno risponde su 127.0.0.1:" + port +
                                     " (server esterno SERVER-COOP, o un'altra sessione host): non avvio niente");
                        return false;
                    }

                    ModLog.Warn("[Host] dettaglio: la sonda dice occupata ma nessuno risponde su 127.0.0.1:" +
                                port + " (TIME_WAIT o NIC occupata): provo l'avvio vero");
                }

                CoopServer started = new CoopServer(MAX_PLAYERS, password ?? string.Empty);
                if (!started.Start(port))
                {
                    // `Server.Start` ritorna `false` su `SocketException` e su `InvalidOperationException`
                    // ("Port was already occupied"): e' l'avvio vero che dice l'ultima parola.
                    LastError = "la porta " + port + " e' gia' occupata (server esterno o altra sessione host)";
                    ModLog.Error("[Host] porta occupata");
                    return false;
                }

                _server = started;
                _instances = 1;

                ModLog.Info("[Host] server avviato porta=" + port + " istanze=" + _instances);

                // ⭐ L'INDIRIZZO DA DIRE ALL'ALTRA MACCHINA. Non e' cosmesi: senza, chi ospita deve
                // andarselo a cercare, ed e' l'unico dato che serve al peer per entrare.
                ModLog.Info("[Host] indirizzo locale=" + LocalAddress());

                // ⭐ IL MARKER DEL BIND — e' la riga che dice, dal LOG DEL GIOCO, se il loopback e'
                // coperto. Non si scrive "0.0.0.0" perche' lo dice il codice: si CHIEDE all'ascoltatore
                // (`Server.ListeningAddresses()` -> `SimpleTcpServer.GetListeningIPs()`) quali socket
                // esistono davvero. Prima di questo fix la risposta sarebbe stata l'elenco delle NIC
                // (es. `192.168.1.27,...`, senza `127.0.0.1`); col fix e' `0.0.0.0`, che significa
                // "loopback + LAN". La porta e' quella vera, la stessa che il dial usa.
                ModLog.Info(MarkerBind + started.ListeningAddresses() + " porta=" + port +
                            " (letto dall'ascoltatore)");
                return true;
            }
            catch (Exception e)
            {
                // ⚠️ UN SERVER CHE NON PARTE NON DEVE ROMPERE IL GIOCO: qui siamo nel `OnGUI` della
                // schermata di connessione, e un'eccezione che sfugge a `OnGUI` e' un errore di
                // Unity ogni frame, per sempre. Si torna `false` e si va avanti.
                _server = null;
                _instances = 0;
                LastError = "errore: " + e.GetType().Name + ": " + e.Message;
                ModLog.Error("[Host] errore: " + e.GetType().Name + ": " + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Ferma l'host e libera la porta. Chiamabile sempre (anche se non c'e' nessun host: in quel
        /// caso non fa NIENTE e non stampa niente, cosi' "Disconnect" non scrive `[Host] server
        /// fermo` quando host non ce n'era).
        ///
        /// Non lancia: viene chiamata dal pulsante, dal `Disconnect` e da `OnDispose`.
        /// </summary>
        internal static void Stop()
        {
            CoopServer running = _server;
            _server = null;

            if (running == null)
                return;

            _instances = 0;

            try
            {
                running.Stop();
            }
            catch (Exception e)
            {
                // `Server.Stop` gia' non lancia; questa e' la cintura per un guasto che nessuno ha
                // previsto. La porta va considerata libera lo stesso: l'oggetto e' staccato.
                ModLog.Error("[Host] errore: " + e.GetType().Name + ": " + e.Message);
            }

            ModLog.Info("[Host] server fermo");
        }

        /// <summary>
        /// ⭐ "IL LOOPBACK RISPONDE ADESSO?" — UN tentativo, e con che indirizzo.
        ///
        /// Ritorna <see cref="LOOPBACK_IPV4"/> se `127.0.0.1:porta` accetta una connessione, altrimenti
        /// <see cref="LOOPBACK_NAME"/> se e' `localhost` ad accettarla (cioe' il nome risolve su `::1`,
        /// IPv6: con un bind IPv4-only come quello del client, un dial al NOME sarebbe l'unico modo),
        /// altrimenti `null`.
        ///
        /// ⚠️ NON e' una `Sleep` e non e' un'attesa fissa: e' una misura. Chi la chiama la ripete (una
        /// volta per frame, vedi `ConnectionDisplay.HostDial`) finche' non risponde o finche' non scade
        /// il tempo - e la connessione vera parte **dopo**, quando questa ha risposto.
        ///
        /// ⚠️ ORDINE DELIBERATO: prima `127.0.0.1` e poi `localhost`. `127.0.0.1` e' l'unico indirizzo
        /// che non passa dal resolver, quindi non puo' essere dirottato dal file hosts; il nome serve
        /// solo come RIPIEGO per il caso IPv6. Cosi' il caso normale non paga nemmeno la risoluzione.
        ///
        /// ⚠️ La sonda e' una connessione TCP vera, aperta e chiusa subito: **il server la vede** (due
        /// righe `[Server] Client connected` / `Client disconnected`) e non e' un giocatore (non manda
        /// nessun `Intro`, quindi non entra in `connectedPlayers`). E' il prezzo per misurare davvero
        /// "la porta risponde" invece di indovinarlo: un bind di prova direbbe "occupata" anche se il
        /// listener non accetta, e bloccherebbe l'utente su un falso negativo.
        ///
        /// Non lancia mai: ogni guasto vale "non ha risposto".
        /// </summary>
        internal static string ProbeLoopback(int port, int attemptTimeoutMs)
        {
            try
            {
                if (Answers(LOOPBACK_IPV4, port, attemptTimeoutMs))
                    return LOOPBACK_IPV4;

                if (Answers(LOOPBACK_NAME, port, attemptTimeoutMs))
                    return LOOPBACK_NAME;

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// "Qualcuno ACCETTA una connessione su host:porta, entro <paramref name="timeoutMs"/>?"
        ///
        /// `false` su rifiuto immediato (`Connection refused`: nessuno ascolta) e su timeout (nessuna
        /// risposta: prudenza). A differenza di <see cref="SomeoneListens"/> - che sul dubbio dice
        /// `true` perche' li' il costo dell'errore e' "avvio un secondo server" - qui il dubbio vale
        /// `false`, perche' il costo e' solo "riprovo al frame dopo", e perche' chi chiama prova
        /// comunque a connettersi alla fine.
        /// </summary>
        private static bool Answers(string host, int port, int timeoutMs)
        {
            TcpClient probe = null;

            try
            {
                probe = new TcpClient();
                IAsyncResult pending = probe.BeginConnect(host, port, null, null);

                if (!pending.AsyncWaitHandle.WaitOne(timeoutMs))
                    return false;

                probe.EndConnect(pending);
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (probe != null)
                {
                    try
                    {
                        probe.Close();
                    }
                    catch (Exception)
                    {
                        // Niente: la sonda ha gia' fatto il suo lavoro.
                    }
                }
            }
        }

        /// <summary>
        /// "Qualcuno RISPONDE su `127.0.0.1:porta`?" — la seconda opinione sulla porta.
        ///
        /// `true` = c'e' un socket in ascolto (o comunque qualcosa che non rifiuta): la porta e' di
        /// qualcun altro e non si avvia niente. `false` = connessione RIFIUTATA subito: nessuno
        /// ascolta su loopback, quindi la sonda che diceva "occupata" parlava di TIME_WAIT o di
        /// un'altra interfaccia, non di un server da rispettare.
        ///
        /// ⚠️ In caso di dubbio si risponde `true` (timeout di 1 s): fra "avvio un secondo server
        /// sopra quello di qualcuno" e "dico all'utente di riprovare", il secondo errore costa molto
        /// meno del primo.
        /// </summary>
        private static bool SomeoneListens(int port)
        {
            TcpClient probe = null;

            try
            {
                probe = new TcpClient();
                IAsyncResult pending = probe.BeginConnect("127.0.0.1", port, null, null);

                if (!pending.AsyncWaitHandle.WaitOne(1000))
                    return true;

                probe.EndConnect(pending);
                return true;
            }
            catch (SocketException)
            {
                // Connection refused: nessuno in ascolto.
                return false;
            }
            catch (Exception)
            {
                return true;
            }
            finally
            {
                if (probe != null)
                {
                    try
                    {
                        probe.Close();
                    }
                    catch (Exception)
                    {
                        // Niente.
                    }
                }
            }
        }

        /// <summary>
        /// La prova di "porta libera": lega la porta e la libera subito. Serve SOLO a dare un
        /// messaggio chiaro - l'avvio vero e' `Server.Start`, che ha la sua strada d'errore.
        ///
        /// ⚠️ `ExclusiveAddressUse = true` prima del `Start`: su Windows e' cio' che fa fallire la
        /// prova anche se l'altro socket ha chiesto il riuso dell'indirizzo. Senza, una porta
        /// occupata poteva risultare libera e si sarebbe scoperto solo alla connessione.
        ///
        /// ⚠️ C'e' una finestra di pochi millisecondi fra la chiusura della sonda e il `bind`
        /// dell'engine: se qualcuno prende la porta proprio li', lo dice `Server.Start` (stesso
        /// marker). Non e' eliminabile senza riscrivere l'engine, e non vale la pena.
        /// </summary>
        private static bool IsPortFree(int port)
        {
            TcpListener probe = null;

            try
            {
                probe = new TcpListener(IPAddress.Any, port);

                try
                {
                    probe.ExclusiveAddressUse = true;
                }
                catch (Exception)
                {
                    // Su qualche runtime la proprieta' non e' impostabile prima del `Start`: si prova
                    // lo stesso, la sonda resta utile nella maggior parte dei casi.
                }

                probe.Start();
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
            finally
            {
                if (probe != null)
                {
                    try
                    {
                        probe.Stop();
                    }
                    catch (Exception)
                    {
                        // Niente: la sonda ha gia' fatto il suo lavoro.
                    }
                }
            }
        }

        /// <summary>
        /// ⭐ L'INDIRIZZO IPv4 DI QUESTA MACCHINA SULLA RETE LOCALE, cioe' quello che il peer deve
        /// scrivere nel campo "Server IP" per entrare nella sessione host.
        ///
        /// Solo informazione: non decide niente e non apre niente. Se la risoluzione fallisce (nessuna
        /// rete, nome non risolvibile) si dice "sconosciuto" e si va avanti - l'host funziona lo
        /// stesso, e in quel caso l'IP lo si trova a mano.
        ///
        /// Il risultato e' in cache: la risoluzione del nome della macchina puo' costare decine di
        /// millisecondi, e questa funzione e' chiamata dal `OnGUI` (una volta per avvio, ma il costo
        /// non ha motivo di essere pagato due volte).
        /// </summary>
        private static string LocalAddress()
        {
            if (_localAddress != null)
                return _localAddress;

            try
            {
                IPAddress[] addresses = Dns.GetHostEntry(Dns.GetHostName()).AddressList;
                foreach (IPAddress address in addresses)
                {
                    if (address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    if (IPAddress.IsLoopback(address))
                        continue;

                    _localAddress = address.ToString();
                    return _localAddress;
                }
            }
            catch (Exception)
            {
                // Nessuna rete, o nome non risolvibile: non e' un errore dell'host.
            }

            _localAddress = "sconosciuto";
            return _localAddress;
        }

        private static string _localAddress;
    }
}
