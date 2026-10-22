using Mono.Cecil; using System; using System.Linq; using System.IO; using System.Collections.Generic;
class P {
  // Tipo -> nomi dei metodi. Copre gioco E mod: la mod si patcha da sola.
  static Dictionary<string, HashSet<string>> idx = new Dictionary<string, HashSet<string>>();
  static int tot = 0, bad = 0, tmiss = 0, normale = 0, divisa = 0, incompleta = 0;

  // Il bersaglio si verifica allo stesso modo in cui ci arriva Harmony: tipo esistente, metodo esistente.
  static void Verifica(string tn, string mn, string owner) {
    tot++;
    HashSet<string> ms;
    if (!idx.TryGetValue(tn, out ms)) {
      tmiss++;
      Console.WriteLine("  TIPO ASSENTE  : " + tn + "." + mn + "   [" + owner + "]");
      return;
    }
    if (!ms.Contains(mn)) {
      bad++;
      Console.WriteLine("  METODO ASSENTE: " + tn + "." + mn + "   [" + owner + "]");
    }
  }

  // Un [HarmonyPatch(typeof(T))]: la META' TIPO della forma divisa (null se l'attributo non e' questa forma).
  static string TipoDa(CustomAttribute ca) {
    var a = ca.ConstructorArguments;
    if (a.Count == 1 && a[0].Value is TypeReference) return ((TypeReference)a[0].Value).FullName;
    return null;
  }

  // Un [HarmonyPatch("nome")]: la META' NOME della forma divisa (null se l'attributo non e' questa forma).
  static string NomeDa(CustomAttribute ca) {
    var a = ca.ConstructorArguments;
    if (a.Count == 1 && a[0].Value is string) return (string)a[0].Value;
    return null;
  }

  // LA REGOLA PRUDENTE: fra i due attributi divisi non deve esserci NIENT'ALTRO che altri [Harmony*].
  // Righe consecutive, o al massimo altri attributi Harmony in mezzo. Qualunque altra cosa interrompe
  // l'accoppiamento: niente coppie a distanza, mai.
  static bool Vicini(IList<CustomAttribute> cas, int a, int b) {
    for (int k = a + 1; k < b; k++)
      if (!cas[k].AttributeType.Name.StartsWith("Harmony", StringComparison.Ordinal)) return false;
    return true;
  }

  // Esiti: 0 tutti i bersagli esistono; 1 almeno un bersaglio non esiste (o c'e' una patch
  // incompleta); 3 nessun [HarmonyPatch] trovato = la sonda non ha misurato niente.
  static void Main(string[] a) {
    var modPath = a[0]; var gamePath = a[1]; var gameDir = Path.GetDirectoryName(gamePath);
    var r = new DefaultAssemblyResolver(); r.AddSearchDirectory(gameDir);
    var game = AssemblyDefinition.ReadAssembly(gamePath, new ReaderParameters{AssemblyResolver=r});
    var mod = AssemblyDefinition.ReadAssembly(modPath, new ReaderParameters{AssemblyResolver=r});

    foreach (var asm in new[]{ game, mod }) {
      foreach (var t in asm.MainModule.GetTypes()) {
        if (!idx.ContainsKey(t.FullName)) idx[t.FullName] = new HashSet<string>();
        foreach (var m in t.Methods) idx[t.FullName].Add(m.Name);
      }
    }

    foreach (var t in mod.MainModule.GetTypes()) {
      var cas = t.CustomAttributes;
      var sospesiTipo = new List<int>();   // [HarmonyPatch(typeof(X))] in attesa del nome

      for (int i = 0; i < cas.Count; i++) {
        var ca = cas[i];
        if (ca.AttributeType.Name != "HarmonyPatch") continue;
        var args = ca.ConstructorArguments;

        // FORMA NORMALE: un attributo con tipo + nome.
        if (args.Count >= 2) {
          var tr = args[0].Value as TypeReference;
          var mn = args[1].Value as string;
          if (tr == null || mn == null) continue;   // forme che non dichiarano tipo+metodo (es. MethodType)
          normale++;
          Verifica(tr.FullName, mn, t.Name);
          continue;
        }

        // FORMA DIVISA, prima meta': il tipo, che resta in attesa del nome.
        if (TipoDa(ca) != null) { sospesiTipo.Add(i); continue; }

        // FORMA DIVISA, seconda meta': il nome, che cerca il suo tipo QUI VICINO.
        var nome = NomeDa(ca);
        if (nome == null) continue;
        int preso = -1;
        for (int k = sospesiTipo.Count - 1; k >= 0; k--) {
          if (Vicini(cas, sospesiTipo[k], i)) { preso = sospesiTipo[k]; sospesiTipo.RemoveAt(k); break; }
        }
        if (preso < 0) {
          incompleta++;
          Console.WriteLine("  PATCH INCOMPLETA: [HarmonyPatch(\"" + nome + "\")] senza il suo [HarmonyPatch(typeof(...))]   [" + t.Name + "]");
          continue;
        }
        var tnDiviso = TipoDa(cas[preso]);
        divisa++;
        Console.WriteLine("  PATCH DIVISA  : " + tnDiviso + "." + nome + "   [" + t.Name + "]");
        Verifica(tnDiviso, nome, t.Name);
      }

      // Un tipo dichiarato e mai accoppiato: O e' un idioma legittimo (il nome sta sui METODI della
      // patch, come vuole Harmony), O e' una patch incompleta. Le due cose si distinguono guardando i
      // metodi; se non c'e' nessun nome, e' un ERRORE e si dichiara.
      foreach (int i in sospesiTipo) {
        var tn = TipoDa(cas[i]);
        var nomi = new List<string>();
        foreach (var m in t.Methods) {
          foreach (var ca in m.CustomAttributes) {
            if (ca.AttributeType.Name != "HarmonyPatch") continue;
            var n = NomeDa(ca);
            if (n != null && !nomi.Contains(n)) nomi.Add(n);
          }
        }
        if (nomi.Count == 0) {
          incompleta++;
          Console.WriteLine("  PATCH INCOMPLETA: [HarmonyPatch(typeof(" + tn + "))] senza il nome del metodo   [" + t.Name + "]");
          continue;
        }
        foreach (var n in nomi) {
          divisa++;
          Console.WriteLine("  PATCH DIVISA  : " + tn + "." + n + "   [" + t.Name + ", nome sul metodo]");
          Verifica(tn, n, t.Name);
        }
      }
    }

    // ZERO BERSAGLI non e' "tutti validi": e' "la sonda non ha misurato niente". Succede quando legge
    // la DLL sbagliata (o una mod senza patch), e prima di questa guardia l'esito era verde con 0
    // controlli - lo stesso difetto di `exit $null` = 0, un livello piu' in basso. Il codice 3 lo
    // distingue da 1 ("ho misurato e ci sono bersagli rotti"), cosi' il .ps1 puo' dire la cosa vera.
    if (tot == 0) {
      Console.WriteLine("  bersagli controllati : 0");
      Console.WriteLine("  ESITO                : NESSUN BERSAGLIO TROVATO - la verifica NON ha misurato niente");
      Environment.Exit(3);
    }

    bool ok = bad == 0 && tmiss == 0 && incompleta == 0;
    string esito = "tutti validi";
    if (!ok) esito = (bad + tmiss > 0 ? "CI SONO BERSAGLI INESISTENTI" : "")
                   + (incompleta > 0 ? ((bad + tmiss > 0 ? " E " : "") + "CI SONO PATCH INCOMPLETE") : "");

    Console.WriteLine("  bersagli controllati : " + tot);
    Console.WriteLine("    forma normale      : " + normale);
    Console.WriteLine("    forma divisa       : " + divisa);
    Console.WriteLine("  tipo assente         : " + tmiss);
    Console.WriteLine("  METODO assente       : " + bad);
    Console.WriteLine("  patch incompleta     : " + incompleta);
    Console.WriteLine("  ESITO                : " + esito);
    Environment.Exit(ok ? 0 : 1);
  }
}
