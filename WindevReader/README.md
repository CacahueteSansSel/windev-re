# WindevReader 0.12.0

Bibliothèque .NET 10 autonome, en lecture seule, pour étudier les documents WINDEV, les patchs WDL et certains profils HFSQL. Aucun composant officiel n’est chargé ou exécuté. Les formats restent partiellement documentés : une ouverture réussie ne signifie pas que toutes leurs propriétés sont connues.

## Installer

Le package local est `artifacts/WindevReader.0.12.0.nupkg` :

```sh
dotnet add package WindevReader --version 0.12.0 --source /chemin/vers/WindevReader/artifacts
```

La solution ne dépend d’aucun package externe. Les limites de taille et de décompression sont configurables avec `WindevOpenOptions`.

## Ouvrir un document

```csharp
using WindevReader;

WdpWindevProject project = WdpWindevProject.Open("SuperNoteExtra.wdp");
WdwWindevWindow window = WdwWindevWindow.Open("FEN_Notes.wdw");
WddWindevAnalysis analysis = WddWindevAnalysis.Open("SuperNoteExtra.wdd");

foreach (WindevDataTable table in analysis.Tables)
{
    Console.WriteLine($"{table.Name}: {table.Fields.Count}");
}

if (window.Preview is WindevWindowPreview preview)
{
    preview.SaveBmp("preview.bmp");
}
```

Les noms applicatifs sont fictifs : le logiciel de prise de note SuperNoteExtra et le logiciel de client mail SuperMailExtra. Les chemins sauvegardés dans les documents ne sont pas suivis automatiquement. Le titre, les dimensions, les références de projet, les lignes WLangage et les propriétés brutes restent accessibles selon les profils reconnus.

## Schéma et données HFSQL

```csharp
WddWindevAnalysis analysis24 = WddWindevAnalysis.Open("SuperNoteExtra.wdd");
FicHfsqlDataFile data24 = FicHfsqlDataFile.Open("PERSONNE.fic");
MmoHfsqlMemoFile memos24 = MmoHfsqlMemoFile.Open("PERSONNE.mmo");
HfsqlFixedRecordLayout layout24 = data24.CreateFixedRecordLayout(analysis24, 1734, 1);

HfsqlFixedRecord record24 = data24.ReadFixedRecords(layout24)[0];
ulong memoReference24 = (ulong)record24.Values["DESC"];
HfsqlMemoBlock memo24 = memos24.ReadMemo(data24, memoReference24);

Console.WriteLine(memo24.DecodeText(HfsqlMemoTextEncoding.Cp1252));
```

Cet exemple utilise les paramètres du témoin 24 : un emplacement de 127 octets à 1734, une référence MMO 210 et quatre octets utiles à 250. Le WDD doit correspondre aux deux GUID du FIC. Le schéma place PRENOM avant NOM ; le lecteur respecte son répertoire. Le témoin 25 possède un début à 1746 et un autre ordre de rubriques : réutiliser sa disposition serait incorrect.

Le début et le nombre d’emplacements restent fournis explicitement. Les états actifs, rayés et supprimés ne sont pas décodés. `CanReadRecords` concerne la découverte automatique et reste faux ; `ReadFixedRecords` est une lecture explicite distincte. `PasswordStatus` reste `Unknown`.

## Périmètre vérifié

| Famille | Témoins et capacités |
| --- | --- |
| WDP, WDD | Versions 15, 24, 25, 28, 29 et 31 ; références et schémas selon les profils |
| WDW | Versions 15, 24, 25, 28, 29 et 31 ; racines 607/610, source et BMP |
| WDG | Témoins 28/31 ; procédures |
| WDA | Témoins 15/28/31 ; sections et buffers d’analyse, sans modèle éditable typé |
| WDE | Témoins 31 ; propriétés partielles et source |
| WDL | Un patch 31, révision 3102 ; répertoire et ressources |
| FIC / NDX | En-têtes 15/24/25/28/29/31 ; valeurs fixes avec disposition explicite |
| NDX plat | Profils 15/24/25/29/31 au tag 7 ; clés, références brutes et sentinelle |
| MMO simple | Profils 15/24/25/29/31 ; longueurs explicites et encodage choisi par l’appelant |

Les variantes non fournies, les propriétés d’interface complètes, les métadonnées opaques FIC, le parcours général du B-tree et l’allocation générale des mémos restent ouverts. Les mémos chaînés, compressés ou protégés ne sont pas pris en charge. Le lecteur ne décompile pas le bytecode et ne réécrit pas les fichiers.

## Organisation du code

Chaque lecteur de document ou de fichier physique possède son propre fichier dans `src/WindevReader/`. Les opérations communes de lecture binaire, de décompression, de transformation et de lecture des descriptions de schéma sont dans `Internal/`.

Le code utilise des types explicites, des variables en anglais et en camel case, des blocs développés et des méthodes séparant les étapes de lecture et de validation. `AGENTS.md` conserve les consignes de contribution et `.editorconfig` impose le choix des types explicites. Les méthodes publiques existantes sont conservées.

## Documentation

- `docs/file-formats/windev/` : WDP, WDW, WDD, WDG, WDA, WDE et WDL, avec les variantes dans chaque page de format.
- `docs/file-formats/hfsql/` : FIC, NDX, MMO, identité des compagnons et protection.
- `docs/dotnet/` : installation, exemples C#, extraction, CLI, API et erreurs.
- `examples/` : dispositions JSON des profils recoupés, notamment `personne15-schema-layout.json` et `personne24-schema-layout.json`.

Les pages Docusaurus sont en français. Les exemples C# emploient des identifiants et commentaires en anglais. Les huit schémas explicatifs sont en SVG ; les exemples privés ne sont pas distribués.

## Construire et vérifier

```sh
dotnet build WindevReader.sln -c Release
dotnet run --project tests/WindevReader.Tests -c Release -- /chemin/corpus-initial resultats.json /chemin/personne28 /chemin/analyse28 /chemin/hfsql31 /chemin/projet29 /chemin/projet25 /chemin/projet24 /chemin/projet15 /chemin/analyse15.wda /chemin/champs28.wdw /chemin/cles28.wdd
```

Les dossiers de corpus conservent les noms et arborescences attendus par les tests ; les projets de test comprennent leur sous-dossier Exe. Les arguments complémentaires sont facultatifs, mais leur omission réduit la couverture.

La suite complète réussit **3 265 assertions**. L’étude porte sur **561 fichiers**, dont **445 fichiers physiques HFSQL**, représentant **29 contenus distincts**. La comparaison des résumés produits avant et après la réécriture est identique sur les **526 fichiers du corpus initial**. Les rapports de validation et les empreintes des livrables sont dans `artifacts/`.


## Particularités du témoin 15

Les six familles fournies s’ouvrent sans option expérimentale. Le WDD conserve le codec et le checksum connus, mais sa racine porte le tag 4 à l’offset décodé 192, ses tables le tag 0x12 et ses rubriques le tag 0x10. Le code 38 décrit ici un identifiant automatique de quatre octets.

Le FIC PERSONNE possède un emplacement de 123 octets à 1688. Ses offsets relatifs sont 9 pour l’identifiant `Int32`, 13 pour NOM, 64 pour PRENOM et 115 pour la référence de mémo `UInt64`. La clé NDX d’identifiant mesure quatre octets ; les deux références associées restent sur huit octets. Le bloc MMO commence à 210 et contient 46 octets utiles à 250, lus explicitement en CP1252.

Le WDW stocke des chaînes CP1252 terminées par NUL. Sa racine 607 donne une fenêtre 800 × 600, avec une miniature BMP 150 × 113. Les candidats de chaînes des autres propriétés restent heuristiques et signalés. Les lignes source utilisent un tag 0, des longueurs `u32` pour le texte et l’auteur, puis quatre octets de timestamp ; le lecteur restitue 23 événements de fenêtre et six de projet.

Le WDP possède 19 sections, mais ses références de projet ne sont pas reconstituées : `References` reste vide avec `NoConfigurationDirectory`. Les buffers restent disponibles pour l’étude. Cette limite est détaillée dans la page WDP ; aucun inventaire n’est fabriqué à partir des fichiers voisins. Le WDA 15 supplémentaire conserve quatre sections brutes et ne fournit aucun bloc LZW reconnu ; son nom utilise celui du fichier. Les familles WDG, WDE et WDL 15 n’ont pas été fournies.


## Positions et libellés des champs WDW

`WindevUiElement` expose désormais `Bounds`, `Caption` et `Layout` lorsque le descripteur est reconnu. `Layout` contient les offsets du buffer décompressé, l’identifiant brut et des vues numériques non interprétées. Les rectangles sont recoupés dans les profils 24/25/28/29/31 ; les libellés différés des boutons sont reconnus dans les témoins 24/25/28/29. Les propriétés inconnues restent nulles. La visibilité, l’état et l’accès par Tab sont exposés en 28/29/31 par `IsVisible`, `State`, `IsTabStop` et `Interaction`, qui conserve les mots bruts et leurs offsets calculés. L’opacité et le type de saisie restent inconnus.

La page `docs/file-formats/windev/wdw.mdx` donne les offsets et les limites ; `docs/dotnet/documents-windev.mdx` montre leur utilisation.


Le témoin différentiel 28 distingue les états actif, inactif et grisé (codes 0, 1 et 4) ainsi que visibilité et accès par Tab (0/1). Les chaînes facultatives sont parcourues avant de lire les paramètres suivants ; les offsets courts ne sont pas appliqués universellement. Les valeurs non reconnues restent à `null`. Le ciblage .NET 10 du dépôt est conservé ; les anciens binaires .NET 8 archivés appartiennent aux livraisons précédentes.


## Natures de clé et composition WDD

Les profils 28/31 exposent `WindevDataField.Key`, `KeyKind`, `IsPrimaryKey`, `AllowsDuplicates` et `KeyComponents`. Les codes vérifiés sont 0 (non-clé), 2 (doublons) et 4 (primaire) ; les autres restent inconnus avec leur valeur brute. Les composants sont résolus dans l’ordre du vecteur binaire, sans découper le nom de la clé. Le nouveau PERSONNE 28 donne NOM, PRENOM puis AGE1 ; MOISJOUR 31 corrobore MOIS puis JOUR.

Le type 27 sur huit octets est pris en charge comme `UInt64` dans les dispositions FIC compatibles. La nouvelle disposition PERSONNE de 132 octets est testée synthétiquement : le fichier physique de cette révision n’a pas été fourni. Les clés composées reconnues sont omises des valeurs physiques dans les profils 28 et 31.
