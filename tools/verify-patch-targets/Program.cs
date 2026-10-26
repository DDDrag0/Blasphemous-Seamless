using Mono.Cecil; using System; using System.Linq; using System.IO; using System.Collections.Generic;
class P {
  // Type -> method names. Covers game AND mod: the mod patches itself.
  static Dictionary<string, HashSet<string>> idx = new Dictionary<string, HashSet<string>>();
  static int tot = 0, bad = 0, tmiss = 0, normale = 0, divisa = 0, incompleta = 0;

  // Verify the target the way Harmony resolves it: type exists, method exists.
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

  // From [HarmonyPatch(typeof(T))]: the TYPE half of the split form (null if not this shape).
  static string TipoDa(CustomAttribute ca) {
    var a = ca.ConstructorArguments;
    if (a.Count == 1 && a[0].Value is TypeReference) return ((TypeReference)a[0].Value).FullName;
    return null;
  }

  // From [HarmonyPatch("name")]: the NAME half of the split form (null if not this shape).
  static string NomeDa(CustomAttribute ca) {
    var a = ca.ConstructorArguments;
    if (a.Count == 1 && a[0].Value is string) return (string)a[0].Value;
    return null;
  }

  // Prudent rule: between the two split attributes only other [Harmony*] attributes may appear.
  // No distant pairs, ever.
  static bool Vicini(IList<CustomAttribute> cas, int a, int b) {
    for (int k = a + 1; k < b; k++)
      if (!cas[k].AttributeType.Name.StartsWith("Harmony", StringComparison.Ordinal)) return false;
    return true;
  }

  // SCRIPT PROTOCOL (exit code alone is not enough).
  //
  // A crash exits with a code that is not ours (unhandled exception = its HRESULT; killed process
  // = another; Environment.Exit(1) from a library = the SAME 1 that used to mean "broken targets").
  // So each MEASURED outcome prints, as the LAST line, a machine marker `SONDA-ESITO: <outcome>`,
  // and the script trusts the code only if it matches the marker. Allowed combinations:
  //     0 + TUTTI-VALIDI        probe ran: all targets exist
  //     2 + BERSAGLI-INVALIDI   probe ran: broken/incomplete targets exist
  //     3 + ZERO-BERSAGLI       probe found NO attribute: nothing measured
  //     4 + ERRORE              probe stopped halfway; `SONDA FALLITA: <type>: <msg>` says why
  // Anything else = "could not verify". Using 2 instead of 1 makes it impossible to confuse
  // "broken targets" with a crash exit 1.
  const string MARCA_ESITO = "SONDA-ESITO";
  const string MARCA_CONTA = "SONDA-CONTA";

  // Declare outcome and exit. Marker printed AFTER counts: it is the last line of the output.
  static void Dichiara(int codice, string esito) {
    Console.WriteLine("  " + MARCA_CONTA + ": totale=" + tot + " normale=" + normale + " divisa=" + divisa
                    + " tipoassente=" + tmiss + " metodoassente=" + bad + " incompleta=" + incompleta);
    Console.WriteLine("  " + MARCA_ESITO + ": " + esito);
    Console.Out.Flush();
    Environment.Exit(codice);
  }

  static void Main(string[] a) {
    try { Esegui(a); }
    catch (Exception e) {
      // The probe could not check: declare it (code 4 + ERRORE marker) instead of letting the
      // raw exception reach whoever reads the exit code. Without this, a BadImageFormatException
      // on an unreadable DLL was read as "patches are wrong".
      Console.Out.Flush();
      var causa = e;
      while (causa.InnerException != null) causa = causa.InnerException;
      var perche = e.GetType().Name + ": " + e.Message;
      if (!ReferenceEquals(causa, e)) perche += "; cause: " + causa.GetType().Name + ": " + causa.Message;
      Console.WriteLine("  SONDA FALLITA: " + perche);
      Console.WriteLine("  SONDA TRACCIA:");
      Console.WriteLine(e.ToString());
      Console.WriteLine("  " + MARCA_ESITO + ": ERRORE");
      Console.Out.Flush();
      Environment.Exit(4);
    }

    // `Esegui` never returns: every path declares an outcome and exits. If it ever returns -
    // a deleted line, an `Environment.Exit` turned `return` - the default is NOT 0: it is an
    // undeclared outcome, i.e. "could not verify". A false green is the defect this file exists
    // to prevent.
    Console.WriteLine("  SONDA FALLITA: the probe returned without declaring an outcome");
    Console.WriteLine("  " + MARCA_ESITO + ": ERRORE");
    Console.Out.Flush();
    Environment.Exit(4);
  }

  static void Esegui(string[] a) {
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
      var sospesiTipo = new List<int>();   // [HarmonyPatch(typeof(X))] awaiting its name

      for (int i = 0; i < cas.Count; i++) {
        var ca = cas[i];
        if (ca.AttributeType.Name != "HarmonyPatch") continue;
        var args = ca.ConstructorArguments;

        // NORMAL FORM: single attribute carrying type + method.
        if (args.Count >= 2) {
          var tr = args[0].Value as TypeReference;
          var mn = args[1].Value as string;
          if (tr == null || mn == null) continue;   // shapes without type+method (e.g. MethodType)
          normale++;
          Verifica(tr.FullName, mn, t.Name);
          continue;
        }

        // SPLIT FORM, first half: the type, kept pending the name.
        if (TipoDa(ca) != null) { sospesiTipo.Add(i); continue; }

        // SPLIT FORM, second half: the name, which looks for its type NEARBY.
        var nome = NomeDa(ca);
        if (nome == null) continue;
        int preso = -1;
        for (int k = sospesiTipo.Count - 1; k >= 0; k--) {
          if (Vicini(cas, sospesiTipo[k], i)) { preso = sospesiTipo[k]; sospesiTipo.RemoveAt(k); break; }
        }
        if (preso < 0) {
          incompleta++;
          Console.WriteLine("  PATCH INCOMPLETA: [HarmonyPatch(\"" + nome + "\")] without its [HarmonyPatch(typeof(...))]   [" + t.Name + "]");
          continue;
        }
        var tnDiviso = TipoDa(cas[preso]);
        divisa++;
        Console.WriteLine("  PATCH DIVISA  : " + tnDiviso + "." + nome + "   [" + t.Name + "]");
        Verifica(tnDiviso, nome, t.Name);
      }

      // A type declared but never paired is either a legitimate idiom (the name lives on the
      // patch's METHODS, as Harmony expects) or an incomplete patch; distinguished by looking at
      // the methods. If no name exists at all, it is an ERROR and is reported.
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
          Console.WriteLine("  PATCH INCOMPLETA: [HarmonyPatch(typeof(" + tn + "))] without the method name   [" + t.Name + "]");
          continue;
        }
        foreach (var n in nomi) {
          divisa++;
          Console.WriteLine("  PATCH DIVISA  : " + tn + "." + n + "   [" + t.Name + ", nome sul metodo]");
          Verifica(tn, n, t.Name);
        }
      }
    }

    // ZERO TARGETS is not "all valid": it is "the probe measured nothing". Happens when reading
    // the wrong DLL (or a mod without patches); before this guard the outcome was green with 0
    // checks - the same defect as `exit $null` = 0, one level lower. Code 3 distinguishes it from
    // 1 ("I measured and there are broken targets"), so the .ps1 can report the truth.
    if (tot == 0) {
      Console.WriteLine("  bersagli controllati : 0");
      Console.WriteLine("  RESULT               : NO TARGET FOUND - the check did NOT measure anything");
      Dichiara(3, "ZERO-BERSAGLI");
    }

    bool ok = bad == 0 && tmiss == 0 && incompleta == 0;
    string esito = "all valid";
    if (!ok) esito = (bad + tmiss > 0 ? "SOME TARGETS DO NOT EXIST" : "")
                   + (incompleta > 0 ? ((bad + tmiss > 0 ? " AND " : "") + "SOME PATCHES ARE INCOMPLETE") : "");

    Console.WriteLine("  bersagli controllati : " + tot);
    Console.WriteLine("    forma normale      : " + normale);
    Console.WriteLine("    forma divisa       : " + divisa);
    Console.WriteLine("  tipo assente         : " + tmiss);
    Console.WriteLine("  METODO assente       : " + bad);
    Console.WriteLine("  patch incompleta     : " + incompleta);
    Console.WriteLine("  RESULT               : " + esito);
    Dichiara(ok ? 0 : 2, ok ? "TUTTI-VALIDI" : "BERSAGLI-INVALIDI");
  }
}