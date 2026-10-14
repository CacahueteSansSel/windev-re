# WindevReader — lecteur .NET indépendant

Bibliothèque **.NET 8**, en C# managé, en lecture seule, sans package externe, DLL PC SOFT, WINDEV installé, COM, P/Invoke ou exécution de l’EXE fourni.

Il s’agit d’un lecteur expérimental issu de l’analyse du corpus Origamide / bridge. **L’ouverture binaire et l’extraction du code fonctionnent ; la description sémantique de toutes les propriétés de WINDEV n’est pas complète.** Les zones inconnues restent accessibles, et les limites apparaissent dans `Diagnostics`.

## Utilisation

```csharp
using WindevReader;

var projet = WdpWindevProject.Open("Origamide.wdp");
var fenetre = WdwWindevWindow.Open("FEN_Journal.wdw");
var procedures = WdgWindevGlobalProcedures.Open("ProcéduresGlobales.wdg");
var analyse = WddWindevAnalysis.Open("Origamide.wdd");
var etat = WdeWindevReport.Open("ETAT_JOURNAL.wde");

Console.WriteLine($"{fenetre.Name}: {fenetre.Caption}, {fenetre.Bounds}");
foreach (var table in analyse.Tables)
    foreach (var rubrique in table.Fields)
        Console.WriteLine($"{table.Name}.{rubrique.Name}: {rubrique.Type}, {rubrique.Size}");

foreach (var procedure in procedures.Procedures)
    Console.WriteLine($"{procedure.Name}\n{procedure.Source}");

// Choix automatique par extension :
WindevDocument document = WindevDocument.Open("ETAT_Test.wde");
// Accès aux données non interprétées, avec offsets :
ReadOnlyMemory<byte> section = document.Sections[1].RawData;
var buffers = document.Sections[1].DecodedBlocks;
```

Référencer `src/WindevReader/WindevReader.csproj`, ou la DLL dans `artifacts/lib/net8.0/`, ou installer le `.nupkg` depuis un dossier NuGet local. Les fichiers d’entrée restent intacts.

## Couverture réelle

| Format / classe | Données exposées | Limites |
|---|---|---|
| `.wdp` / `WdpWindevProject` | En-tête, nom, références de configuration, code du projet, sections et chaînes | Réglages section 1 encore opaques ; noms de configuration 28 non identifiés ; configurations multiples non validées |
| `.wdw` / `WdwWindevWindow` | Nom, titre et dimensions du descripteur 610, buffers de propriétés, chaînes, identifiants d’éléments, événements et procédures locales | Types d’éléments inférés par nom ; styles, coordonnées des champs, hiérarchie et liaisons non entièrement interprétés |
| `.wdg` / `WdgWindevGlobalProcedures` | Déclarations globales, noms et sources des procédures, lignes avec auteur/timestamp brut, identifiants d’événements | Bytecode et métadonnées de l’éditeur conservés sans interprétation |
| `.wdd` / `WddWindevAnalysis` | Déchiffrement autonome, décompression, checksum, GUID/génération, tables physiques, rubriques, libellés, masques, types bruts, tailles et flags | Clés, valeurs par défaut, connexions et relations non entièrement interprétées ; schéma interne `0x061D` seulement |
| `.wde` / `WdeWindevReport` | Nom, message sans données, buffers de propriétés, éléments/blocs nommés, événements et source | Mise en page d’impression, styles et sources de données non entièrement interprétés |
| `.wda` / `WdaWindevAnalysisSource` | Accès supplémentaire aux blocs décompressés et chaînes de l’analyse éditable | Lecture à bas niveau ; utiliser `.wdd` pour les tables/rubriques typées |

`Elements` constitue un inventaire d’identifiants candidats, **pas un arbre exhaustif de contrôles**. `InferredKind` est explicitement une inférence ; `NearbyStrings` désigne les chaînes suivantes dans le buffer, pas des propriétés certifiées du contrôle. Les événements restent numérotés : aucun nom d’événement arbitraire n’est inventé.

Les offsets de `Sections` se rapportent au fichier. Ceux de `DecodedBlocks` se rapportent à la section stockée. Les offsets de chaînes, lignes, objets et rubriques se rapportent au buffer décodé correspondant. Pour les éléments graphiques, `BufferOffset` identifie ce buffer au sein de la section 1.

## Compiler et exécuter

```sh
dotnet build WindevReader.sln -c Release -p:UseSharedCompilation=false -m:1 -nr:false
dotnet run --project tools/WindevReader.Cli -- inspect /chemin/Origamide.wdd
dotnet run --project tools/WindevReader.Cli -- json /chemin/FEN_Journal.wdw fenetre.json
dotnet run --project tools/WindevReader.Cli -- code /chemin/ProcéduresGlobales.wdg procedures.wl
dotnet run --project tools/WindevReader.Cli -- corpus /chemin/Origamide corpus.json
```

La sérialisation JSON expose les données structurées et les chaînes ; les mémoires binaires sont exclues du JSON par `[JsonIgnore]`, et restent disponibles dans l’API. Les commandes CLI ne créent un fichier de sortie que si un chemin est fourni.

## Validation reproductible

```sh
dotnet run --project tests/WindevReader.Tests -c Release -- /chemin/Origamide resultats.json
```

Le dossier de corpus doit contenir `analysis.json`, `Origamide/` et `bridge/`, comme le dossier fourni. Les sources privées et les DLL du corpus ne sont pas recopiées dans cette distribution. Les tests ne chargent ni DLL ni EXE.

Les tests vérifient l’enveloppe exacte de chaque fichier, les tailles après décompression, le checksum du WDD, les historiques du schéma, les noms/types/tailles/libellés des 37 rubriques contre une référence JSON indépendante, des procédures et propriétés connues, ainsi que les erreurs de corruption et les limites de mémoire. Le rapport enregistré se trouve dans `artifacts/test-results.json` ; l’inventaire est dans `artifacts/corpus-validation.json`.

Versions de produits observées : **28 et 31**. Toutes les fenêtres/états du corpus sont en 31 ; le support de leurs équivalents en 28 n’est donc pas certifié. Les autres versions sont refusées par défaut. `AllowUntestedVersions = true` autorise un essai structurel, sans garantir la lecture des structures internes. Le byte `0x14` à l’offset 4 désigne la révision du conteneur, **pas WINDEV 20**.

## Erreurs et limites

`WindevFormatException` signale une structure incorrecte, un checksum invalide, une taille incohérente ou une limite dépassée. `UnsupportedWindevFormatException` signale une version/type/encodage non pris en charge. Les budgets configurables de `WindevOpenOptions` limitent le fichier, chaque buffer, la décompression totale, le nombre de buffers et les chaînes.

Ce lecteur ne modifie/réécrit pas les formats, n’exécute pas le WLangage, n’émule pas les fenêtres et ne lit pas les enregistrements HFSQL `.fic/.ndx/.mmo`. Un schéma `.wdd` contenant un autre mode de transformation, notamment une variante protégée non présente dans le corpus, peut être refusé.

Voir `docs/FORMAT.md` pour la structure déterminée et `docs/RESEARCH.md` pour les sources et la méthode.
