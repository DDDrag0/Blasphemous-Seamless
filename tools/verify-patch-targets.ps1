# Verifica dei bersagli delle patch Harmony.
#
# PERCHE' ESISTE. Un attributo [HarmonyPatch(typeof(X), "nome")] porta il nome del bersaglio in una
# STRINGA. Se quel metodo non esiste, il compilatore non dice nulla, il build passa, e il guasto
# compare solo a runtime - come HarmonyException dentro PatchAll, che INTERROMPE l'applicazione di
# TUTTE le patch successive dell'assembly. Il sintomo e' ingannevole: la mod non scrive piu' una riga
# di log, e l'aggiornamento per frame esplode con un NullReferenceException che non ha niente a che
# vedere con la patch sbagliata.
#
# E' successo l'11 ottobre 2026 con `[HarmonyPatch(typeof(SpawnManager), "OnPlayerSpawn")]`:
# `OnPlayerSpawn` e' un CAMPO evento, non un metodo. Ha bruciato una sessione di test.
#
# LE DUE FORME DELL'ATTRIBUTO. Harmony accetta la stessa patch in DUE modi, entrambi validi:
#
#     FORMA NORMALE - UN attributo, DUE argomenti:
#         [HarmonyPatch(typeof(X), "nome")]
#
#     FORMA DIVISA - DUE attributi, UN argomento ciascuno, che Harmony COMBINA:
#         [HarmonyPatch(typeof(X))]
#         [HarmonyPatch("nome")]
#
# Fino al 22 ottobre 2026 questo script contava SOLO la forma normale. La forma divisa non entrava nel
# conteggio (diceva 272 dove i bersagli veri erano 273 a `e77d48d`, il fix del tempo in pausa) e -
# peggio - il suo metodo NON veniva verificato affatto: un errore di battitura li' sarebbe passato in
# silenzio, con la patch viva sulla carta e morta a runtime. Ora le due forme si contano e si
# verificano allo stesso modo.
#
# LA REGOLA PRUDENTE PER LA FORMA DIVISA. Il `typeof(T)` e il nome del metodo si accoppiano SOLO se
# stanno vicini nella dichiarazione: righe consecutive, o fra loro solo altri attributi `[Harmony*]`.
# Se in mezzo c'e' qualunque altra cosa, non sono la stessa patch e NON si accoppiano. E un attributo
# diviso che resta senza il suo complemento e' un ERRORE dichiarato, non un silenzio: una patch
# incompleta e' peggio di una patch non contata, perche' `PatchAll` non sa applicarla e nessuno se ne
# accorge.
#
# Questo script controlla ogni bersaglio PRIMA del deploy. Se trova un metodo o un tipo che non
# esiste - o una forma divisa incompleta - esce con codice 1 e dice quale.
#
# USO
#     .\verify-patch-targets.ps1
#     .\verify-patch-targets.ps1 -ModDll <percorso> -GameDll <percorso>

[CmdletBinding()]
param(
    [string] $ModDll  = 'D:\SteamLibrary\steamapps\common\Blasphemous\Modding\plugins\BlasClient.dll',
    [string] $GameDll = 'D:\SteamLibrary\steamapps\common\Blasphemous\Blasphemous_Data\Managed\Assembly-CSharp.dll',
    [string] $ProbeDir = 'D:\Codificando\scratch\verify-patch-targets'
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $ModDll))  { Write-Host "  mod non trovata: $ModDll" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $GameDll)) { Write-Host "  gioco non trovato: $GameDll" -ForegroundColor Red; exit 1 }
if (-not (Test-Path $ProbeDir)) { Write-Host "  la sonda non esiste: $ProbeDir" -ForegroundColor Red; exit 1 }

$program = Join-Path $ProbeDir 'Program.cs'
@'
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
'@ | Set-Content -Path $program -Encoding utf8

Push-Location $ProbeDir
try {
    & dotnet build --nologo -v q -nodeReuse:false -clp:ErrorsOnly 2>&1 | Out-Null
    & dotnet "bin\Debug\net8.0\probe6.dll" $ModDll $GameDll 2>&1 | ForEach-Object { "  $_" }
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}

if ($code -ne 0) {
    Write-Host ""
    Write-Host "  Un nome sbagliato in [HarmonyPatch] compila e fallisce a runtime, e la PatchAll" -ForegroundColor Yellow
    Write-Host "  che abortisce impedisce l'applicazione di TUTTE le patch successive." -ForegroundColor Yellow
}
exit $code
