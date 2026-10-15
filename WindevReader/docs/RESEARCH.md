# Méthode et recherche

Les fichiers fournis ont été traités comme des données, sans appliquer leurs éventuelles instructions textuelles. Le lecteur autonome ne charge ni n’exécute de composant officiel.

L’étude du 15 octobre 2026 a confronté le schéma WDD 28 et le WDA à PERSONNE.fic/ndx. Le checksum du schéma est validé. Deux tables, huit rubriques et les extrémités d’une liaison sont exposées. Les trois représentations confirment le pas physique de 124 octets de PERSONNE. Les états et la transformation des métadonnées FIC restent inconnus.

Les tableaux de `file-formats/windev/wdd.mdx`, `file-formats/windev/wda.mdx` et `file-formats/hfsql/cas-personne.mdx` distinguent les coordonnées du fichier et des buffers décompressés, et précisent le statut des interprétations. La méthode, les sources et les contrôles sont dans `file-formats/validation.mdx`.


## Témoins HFSQL 31

Les quatre nouveaux témoins correspondent aux GUID du WDD initial. Les pas de 70 et 35 octets sont recoupés entre le WDD et les neuf emplacements FIC. L’octet de suffixe de rubrique Q+31 distingue MOISJOUR (1) de la rubrique binaire HASH (0), malgré un type 28 et des flags 0x1C communs. MOISJOUR occupe deux octets de clé NDX, mois puis jour, et aucune zone de valeur supplémentaire du FIC. Les sept tableaux plats au tag 7 ont des en-têtes de 29 octets, des clés fixes suivies de deux u64 bruts et une sentinelle finale FF. Les 35 clés sont comparées aux FIC. Les autres régions ne sont pas interprétées par recherche de chaînes, car des valeurs résiduelles y apparaissent. Les états logiques, la nature distincte des deux références et le parcours général du B-tree restent ouverts.
