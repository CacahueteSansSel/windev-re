# Structure binaire déterminée

Les offsets sont zéro-indexés et les nombres little-endian. Cette description provient de mesures sur le corpus, pas d’une spécification officielle universelle.

## Conteneur PCS révision 20

| Offset | Taille | Interprétation vérifiée |
|---:|---:|---|
| 0 | 4 | `50 43 53 00` (`PCS\0`) |
| 4 | 4 | Révision du conteneur : 20 |
| 8 | 4 | Type : projet `0x1001`, fenêtre `2`, procédures `7`, état `3`, WDD `0x00070004`, WDA `0x00080002` |
| 12 | 2 | Révision de produit brute |
| 14 | 2 | Version majeure WINDEV : 28 ou 31 dans le corpus |
| 32 | 4 | Offset relatif du payload ; offset absolu = valeur + 32 |
| 70 | 10 | Build produit ASCII, par exemple `01A312018 ` ou `05F280095g` |
| 81 | 4 | Nombre de sections |
| 85 | 5 × nombre | Chaque entrée = tag d’un octet + longueur de quatre octets |

Les sections suivent dans l’ordre de cette table. Toutes leurs longueurs, ajoutées à l’offset de départ, doivent donner exactement la taille du fichier. Les tags sont exposés tels quels : ils ne constituent pas à eux seuls un type universel de compression.

Exemple `ProcéduresGlobales.wdg` de bridge : départ `0xCA`, 9 sections, longueurs `[147,0,5073,24,61,0,0,29,0]`, taille finale 5536. La section 2 commence à `0x15D`.

Les projets placés dans les répertoires `.cpl` sont de petits caches avec deux sections ; ils s’ouvrent, mais ne contiennent pas la même information que le projet complet.

## Enveloppe LZW

À l’intérieur d’une section, une enveloppe vérifiée a la forme :

```
04 | encodedLength:u32 | decodedLength:u32 | 00 00 | encodedBytes[encodedLength]
```

Selon la section, cette enveloppe est précédée de zéro, quatre, douze ou vingt-huit octets, ou se trouve dans une liste de sous-buffers. Le lecteur recherche les candidats et n’accepte que les flux dont les codes, la taille finale et la fin du flux sont cohérents.

Variante LZW mise en œuvre : ordre des bits LSB-first, largeur initiale 9, largeur maximale 16, code 0 = réinitialisation, littéraux 1..256 = octets 0..255, code 257 = fin. Le dictionnaire commence à 258 après le premier littéral et la largeur augmente lorsque le prochain code atteint la puissance de deux correspondante. Le cas « prochain code = chaîne précédente + premier octet » est traité. La longueur déclarée doit être atteinte exactement et le code de fin doit consommer le dernier octet encodé.

Exemple WDA courant, section 0 : longueur stockée 6826 ; préfixe de 4 octets, méthode 04, 6811 octets encodés et 26160 décodés. Aucun zlib n’est nécessaire.

## Chaînes encadrées

```
FF CE 88 F1 | totalLength:u32 | UTF8 bytes | 00 | FE ED 98 E1
```

`totalLength` inclut les marqueurs, la longueur et le NUL : le texte occupe `totalLength - 13` octets. Les deux marqueurs et le NUL final sont validés. Le nom `ProcéduresGlobales` est réellement UTF-8 ; certaines chaînes zéro-terminées des descriptions HFSQL sont Windows-1252. Le lecteur utilise UTF-8 strict puis une conversion Windows-1252 autonome.

## WLangage

La section 2 de projets/fenêtres/procédures/états contient un buffer LZW précédé de quatre octets. Des chaînes de noms suivies du tag de groupe `0D` donnent le propriétaire des événements suivants. Le propriétaire initial est le nom du document.

Un enregistrement source reconnu commence par `15` ou `16`, puis une longueur u32. Sa longueur totale est cette valeur + 5. Il comporte : nombre de lignes à +5, identifiant d’événement u16 à +9, identifiant de langue u16 à +11, puis données auxiliaires. Les lignes commencent à +21.

Chaque ligne observée suit la structure : `03`, chaîne encadrée (ou `00` pour une ligne vide), chaîne encadrée de l’auteur, puis 12 octets de métadonnées dont le premier u32 est un timestamp brut. Les commentaires, accents, tabulations et lignes vides sont conservés. Une chaîne quelconque trouvée dans un fichier n’est pas considérée comme une ligne de code sans cette structure.

Les numéros d’événements ne sont pas convertis en noms, car la correspondance exhaustive n’a pas été validée.

## WDD : transformation héritée et schéma

La section 1 commence par deux u16 laissés en clair : `0x0060`, `0x061D`. Les octets à partir de +4 sont transformés. Leur décodage révèle une enveloppe LZW ; la sortie est préfixée des quatre premiers octets d’origine pour restituer le buffer du schéma.

La clé de 16 octets est dérivée d’une séquence interne de 10 octets `[15,80,90,110,56,23,19,8,13,21]`, à l’aide d’une accumulation XOR, d’une transformation symétrique des positions et d’un mélange modulo 16. L’algorithme est entièrement réimplémenté dans `PcsLegacyCipher.cs`, avec arithmétique modulo 256. La transformation dépend de la longueur totale, travaille alternativement sur les deux extrémités du buffer et traite spécialement le centre d’une longueur impaire. Elle n’utilise pas un algorithme AES/RC4 standard.

La clé dérivée observée vaut `730C8F88D842C6639022D79F0F081E83`. Le contrôle indépendant porte sur le checksum du buffer final : u32 à +4 = somme modulo 2^32 des u32 à partir de +8, suivie de la somme des éventuels octets restants.

Le schéma contient huit premières entrées de répertoire de 12 octets à partir de +8 : offset u64 et nombre u32. Les entrées identifiées sont métadonnées d’analyse à +8, tables à +20, relations à +32 et index des noms de tables à +56. Les autres restent nommées `DirectoryN`.

L’index des tables contient des couples `nom:CString + position:u64`. Chaque table pointée commence par `22`, un identifiant u32 et quatre octets réservés. À +9 suivent six CString : nom logique, nom physique, répertoire, libellé, métadonnée réservée, extension. Après un segment fixe de 48 octets se trouvent les GUID table/analyse, deux octets réservés, le nom dupliqué et deux octets réservés. Le pointeur des rubriques suit ; un second index situé 70 octets plus loin fournit les offsets exacts des rubriques et leur nombre. Le lecteur vérifie les noms dupliqués et les comptes.

Une rubrique commence par `20`, identifiant u32, quatre octets réservés, identifiant de table u32, quatre octets réservés. À +17 : nom, libellé, GUID et masque, tous CString. Suivent type brut u16, flags u32, un octet réservé, taille u16 et taille de stockage u16. Les règles ultérieures restent dans `DecodedSchema`.

Types rencontrés : 2 texte, 11 heure, 14 date, 24 identifiant automatique 64 bits, 25 entier 64 bits, 26 entier non signé 32 bits, 27 entier non signé 64 bits, 28 binaire, 29 mémo texte, 34 date-heure, 36 entier non signé 8 bits, 37 booléen. Ces codes binaires diffèrent des codes renvoyés par `HListeRubrique` ; la comparaison aux codes publics est réalisée dans les tests, pas confondue dans l’API.

Toutes les analyses fournies déclarent zéro relation : l’absence est vérifiée dans le répertoire, mais aucune sémantique de relation non vide n’est prétendue validée.

## Fenêtres et états

Les buffers de propriétés de la section 1 sont décompressés indépendamment des événements. Les fenêtres observées portent un descripteur racine 610 (`0x262`) à +4 ; le nom encadré commence à +8. Après la chaîne du nom et les 8 octets d’identifiant, quatre i32 donnent X, Y, largeur et hauteur. Le titre encadré se trouve à `finDeLaChaîneDuNom + 571` dans cette révision ; `00` représente un titre vide. Le lecteur n’applique ces positions qu’au descripteur 610.

Exemples vérifiés : `FEN_Journal`, titre « Journal - Origamide », 1177 × 752 ; `FEN_Rapport1`, titre « Rapport1 », 354 × 126. L’inventaire des éléments secondaires est une inférence par identifiants, corroborée par les propriétaires des événements. La hiérarchie complète des contrôles et la disposition des états restent à déterminer.
