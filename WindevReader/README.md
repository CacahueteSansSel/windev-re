# WindevReader 0.5.0

Bibliothèque .NET 8 autonome, en lecture seule, pour les formats WINDEV et HFSQL étudiés. Aucun composant officiel WINDEV n’est chargé ou exécuté. Les fichiers privés du corpus ne sont pas distribués.

## Installation

Le package local est `artifacts/WindevReader.0.5.0.nupkg` :

```sh
dotnet add package WindevReader --version 0.5.0 --source /chemin/vers/WindevReader/artifacts
```

On peut aussi référencer `src/WindevReader/WindevReader.csproj`. La bibliothèque n’a aucune dépendance NuGet externe.

## Ouvrir un fichier

```csharp
using WindevReader;

IWindevFile fichier = WindevFile.Open("SuperNoteExtra.wdd");
var analyse = WddWindevAnalysis.Open("SuperNoteExtra.wdd");
foreach (var table in analyse.Tables)
    Console.WriteLine($"{table.Name} : {table.Fields.Count} rubriques");
```

`WindevFile.Open` choisit selon le type PCS. Chaque famille possède aussi sa méthode statique `Open` : `WdpWindevProject`, `WdwWindevWindow`, `WdgWindevGlobalProcedures`, `WddWindevAnalysis`, `WdaWindevAnalysisSource`, `WdeWindevReport`, `WdlWindevLibrary`, `FicHfsqlDataFile`, `NdxHfsqlIndexFile`, `MmoHfsqlMemoFile`.

## Apport du WDD/WDA correspondant à PERSONNE

La version 0.5.0 reconnaît les descriptions de tables et rubriques du WDD 28, expose huit rubriques dans deux tables et la liaison entre PERSONNE et PERSONNE_MESSAGE. Les paramètres de contrainte restent bruts.

Le schéma confirme les cinq rubriques physiques PERSONNE : IDPERSONNE (8), NOM (51 stockés pour 50 déclarés), PRENOM (51), AGE1 (4), FLAGADMIN (1). Le WDD et le WDA confirment le pas de 124 octets. Les neuf octets précédant les valeurs restent des données de gestion non interprétées.

```csharp
var fic = FicHfsqlDataFile.Open("PERSONNE.fic");
var disposition = fic.CreateFixedRecordLayout(analyse, dataOffset: 1776, recordCount: 5);
foreach (var emplacement in fic.ReadFixedRecords(disposition))
    Console.WriteLine($"{emplacement.Values["NOM"]} : {emplacement.Values["FLAGADMIN"]}");
```

Utiliser le WDD qui correspond réellement aux deux GUID du FIC. Le nom de présentation SuperNoteExtra est fictif. `CreateFixedRecordLayout` refuse une analyse incompatible, des types non pris en charge ou un pas incohérent. Ce profil fixe a été recoupé sur PERSONNE 28 ; il ne généralise pas sa disposition aux FIC RAD.

Le début 1776 et le nombre 5 restent explicites. Les états actif/rayé/supprimé ne sont pas déterminés ; chaque emplacement est conservé, y compris l’identifiant brut tout à 1. `CanReadRecords` reste `false` pour la découverte automatique complète. `PasswordStatus` reste `Unknown` car l’absence de mot de passe n’est pas détectée par le lecteur.

## CLI

```sh
dotnet artifacts/cli/WindevReader.Cli.dll inspect SuperNoteExtra.wdd
dotnet artifacts/cli/WindevReader.Cli.dll layout PERSONNE.fic SuperNoteExtra.wdd 1776 5 disposition.json
dotnet artifacts/cli/WindevReader.Cli.dll records PERSONNE.fic disposition.json emplacements.json
dotnet artifacts/cli/WindevReader.Cli.dll preview FEN_Notes.wdw previsualisation.bmp
```

Les autres commandes sont `json`, `code`, `extract-resource` et `corpus`. Les fichiers runtime de `artifacts/cli/` doivent rester ensemble.

## Documentation

- `docs/dotnet/index.mdx` : installation, premiers pas, guides, CLI, API et limites.
- `docs/file-formats/index.mdx` : structures binaires, section WINDEV et section HFSQL.
- `docs/file-formats/hfsql/cas-personne.mdx` : étude croisée du FIC, NDX, WDD et WDA.

Les pages sont en français, avec tables d’offsets et quatre schémas SVG. Les exemples utilisent le logiciel fictif de prise de note SuperNoteExtra et le logiciel fictif de client mail SuperMailExtra.

## Compilation et validation

```sh
dotnet build WindevReader.sln -c Release -p:UseSharedCompilation=false -m:1 -nr:false
dotnet run --project tests/WindevReader.Tests -c Release -- /chemin/corpus-initial resultats.json /chemin/personne /chemin/cas-analyse
```

Les chemins des corpus doivent conserver leurs noms et arborescences d’origine, attendus par les tests. Les deux derniers dossiers sont facultatifs et ajoutent respectivement PERSONNE, puis son analyse et ses compagnons. Les quatre fichiers du nouveau cas sont requis dans le dernier dossier. La suite complète passe **2 894 assertions** ; 534 fichiers ont été étudiés, avec 433 fichiers physiques HFSQL représentant 17 contenus distincts. Les exemples C# de la documentation compilent également.

Les rapports se trouvent dans `artifacts/test-results.json`, `artifacts/extension-validation.json` et `artifacts/analysis-validation.json`.

## Portée

Les références de projet, les sources reconnues, les propriétés partielles d’interfaces, les schémas WDD, les BMP de prévisualisation et les ressources du patch WDL étudié sont exposés. Les valeurs FIC fixes sont accessibles avec une disposition explicite ou construite depuis le WDD 28 compatible.

Les métadonnées opaques FIC, les états logiques, le parcours général du B-tree NDX et l’allocation des mémos restent ouverts. Les tableaux plats de clés 31 sont maintenant lisibles. Le WDA expose ses buffers sans reconstruire un modèle éditable complet. Le lecteur n’exécute pas le WLangage, ne décompile pas le bytecode et ne réécrit pas les formats. Les budgets de `WindevOpenOptions` et les diagnostics décrivent les limites de chaque ouverture.


## Nouveaux profils HFSQL 31

Les quatre témoins jours fériés et semaines correspondent au WDD 31 initial. Le lecteur de disposition prend désormais en charge JOUR et MOIS en `UInt8`, ainsi que les dates `Date8Z` (huit chiffres ASCII suivis de NUL, retournés en `string`). Il reconnaît le marqueur brut à Q+31 de la clé composée MOISJOUR et l’omet des valeurs FIC. Une rubrique binaire ordinaire n’est pas reclassée à partir de ses seuls flags.

```csharp
var jours = FicHfsqlDataFile.Open("jour_ferie.fic");
var lignes = jours.ReadFixedRecords(jours.CreateFixedRecordLayout(analyse, 1763, 8));
var semaines = FicHfsqlDataFile.Open("semaine_cours.fic");
var semaine = semaines.ReadFixedRecords(semaines.CreateFixedRecordLayout(analyse, 1627, 1));
var indexJours = NdxHfsqlIndexFile.Open("jour_ferie.ndx");
var cles = indexJours.ReadFlatIndex(3); // profil composé MOIS puis JOUR du témoin
```

Utiliser l’analyse 31 qui correspond à ces GUID. Les dispositions d’exemple sont dans `examples/`. `ReadFlatIndex` expose un profil clair 31 au tag 7 avec en-tête de 29 octets : 35 clés au total sur les sept tableaux étudiés, chaque clé recoupée avec le FIC. La sentinelle finale est vérifiée et exclue. Les deux références sont conservées brutes ; leur rôle exact n’est pas distinguable quand identifiant et rang physique coïncident. Aucun parcours général de B-tree n’est prétendu implémenté. `MaxIndexEntries` borne l’allocation, par défaut à 100 000 entrées par tableau.

```sh
dotnet artifacts/cli/WindevReader.Cli.dll index-entries jour_ferie.ndx 3 cles.json
dotnet run --project tests/WindevReader.Tests -c Release -- /chemin/corpus-initial resultats.json /chemin/personne /chemin/cas-analyse /chemin/hfsql31
```

Le dossier final contient les quatre nouveaux témoins. Leurs fichiers sont lus sans modification et ne sont pas inclus dans la distribution. Le chapitre `docs/file-formats/hfsql/cas-hfsql31.mdx` donne les cartes binaires, les tableaux d’offsets et les inconnues.
