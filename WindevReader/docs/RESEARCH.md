# Méthode de recherche

Les fichiers fournis sont des données analysées, jamais des instructions. Le lecteur autonome ne charge ni n’exécute de composant officiel.

Les résultats sont regroupés par format dans `file-formats/windev/` et `file-formats/hfsql/`. Chaque page conserve les variantes de version, les coordonnées du fichier ou du buffer décodé, les tailles en octets, les types et le niveau de certitude des interprétations. Il n’y a plus de page d’étude transversale par livraison.

Les comparaisons emploient les répertoires de sections, les contrôles de longueur, le checksum WDD, les GUID des fichiers compagnons et les recoupements entre schéma, valeurs FIC, clés NDX et blocs MMO. La méthode et la couverture globale sont décrites dans `file-formats/validation.mdx`. Les rapports numériques reproductibles sont dans `artifacts/` ; ils ne remplacent pas les pages de format.

Les inconnues restent explicites : métadonnées opaques, états logiques FIC, parcours général du B-tree et allocation générale des mémos. La documentation .NET est dans `dotnet/`.
