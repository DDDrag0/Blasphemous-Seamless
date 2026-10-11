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
# Questo script controlla ogni bersaglio PRIMA del deploy. Se trova un metodo o un tipo che non
# esiste, esce con codice 1 e dice quale.
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
using Mono.Cecil; using System.Linq; using System.Collections.Generic;
class P { static void Main(string[] a) {
  var modPath = a[0]; var gamePath = a[1]; var gameDir = System.IO.Path.GetDirectoryName(gamePath);
  var r = new DefaultAssemblyResolver(); r.AddSearchDirectory(gameDir);
  var game = AssemblyDefinition.ReadAssembly(gamePath, new ReaderParameters{AssemblyResolver=r});
  var mod = AssemblyDefinition.ReadAssembly(modPath, new ReaderParameters{AssemblyResolver=r});

  // I bersagli possono stare nel gioco O nella mod stessa: la mod si patcha da sola.
  var idx = new Dictionary<string, HashSet<string>>();
  foreach (var asm in new[]{ game, mod }) {
    foreach (var t in asm.MainModule.GetTypes()) {
      if (!idx.ContainsKey(t.FullName)) idx[t.FullName] = new HashSet<string>();
      foreach (var m in t.Methods) idx[t.FullName].Add(m.Name);
    }
  }

  int tot = 0, bad = 0, tmiss = 0;
  foreach (var t in mod.MainModule.GetTypes()) {
    foreach (var ca in t.CustomAttributes) {
      if (ca.AttributeType.Name != "HarmonyPatch") continue;
      var args = ca.ConstructorArguments;
      if (args.Count < 2) continue;
      var tr = args[0].Value as TypeReference;
      string tn = tr == null ? null : tr.FullName;
      string mn = args[1].Value as string;
      if (tn == null || mn == null) continue;
      tot++;
      if (!idx.ContainsKey(tn)) {
        tmiss++;
        System.Console.WriteLine("  TIPO ASSENTE  : " + tn + "." + mn + "   [" + t.Name + "]");
        continue;
      }
      if (!idx[tn].Contains(mn)) {
        bad++;
        System.Console.WriteLine("  METODO ASSENTE: " + tn + "." + mn + "   [" + t.Name + "]");
      }
    }
  }

  System.Console.WriteLine("  bersagli controllati : " + tot);
  System.Console.WriteLine("  tipo assente         : " + tmiss);
  System.Console.WriteLine("  METODO assente       : " + bad);
  System.Console.WriteLine("  ESITO                : " + ((bad == 0 && tmiss == 0) ? "tutti validi" : "CI SONO BERSAGLI INESISTENTI"));
  System.Environment.Exit((bad == 0 && tmiss == 0) ? 0 : 1);
}}
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
