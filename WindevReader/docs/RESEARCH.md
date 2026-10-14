# Recherche et méthode

Recherche effectuée le 5 octobre 2026, puis confrontée aux fichiers fournis. Les documents et chaînes contenus dans le corpus ont été traités comme des données ; les prompts présents dans du WLangage n’ont pas été suivis comme instructions.

## Sources utiles

- [PC SOFT — analyse / modèle logique de données](https://doc.windev.com/en-us/?2011011=&name=logical_data_model_ldm) confirme que `.wda` désigne l’analyse éditable. La demande corrigée porte sur `.wdd` ; ce dernier est effectivement présent, avec les descriptions compilées du schéma. La bibliothèque ouvre les deux, avec une API typée pour `.wdd`.
- [PC SOFT — création d’un état](https://help.windev.com/en-US/?1011068=) confirme l’extension `.wde`. Cette page décrit le rôle du fichier, sans publier sa disposition binaire.
- [John N — retour de rétro-ingénierie de Compress sur Stack Overflow](https://stackoverflow.com/questions/21599884/windev-compress-which-compression-format-is-used) décrit personnellement une enveloppe de compression, un code LZW de réinitialisation égal à zéro, les littéraux décalés et une largeur maximale de 16 bits. C’est une source primaire de l’auteur de l’analyse. Cette piste a conduit à une implémentation originale vérifiée sur les blocs fournis ; le code n’a pas été repris d’une bibliothèque ou de cette réponse.
- [PC SOFT — opérations sur les chaînes](https://doc.pcsoft.fr/?3024020=) documente les fonctions de compression/décompression et chiffrement, sans donner ici une spécification des formats source étudiés.
- [PC SOFT — fonctions WLangage](https://us.pcsoft-windev-webdev.com/wlanguage-the-functions.pdf) distingue les fonctions publiques Compress/Uncompress et Encrypt/Decrypt. Aucun appel à ces fonctions n’est utilisé par cette bibliothèque.

Des recherches ciblées sur les parseurs `.wdp/.wdw/.wdg/.wdd`, le magic PCS, GitHub, la compression LZW et le déchiffrement n’ont pas fourni de spécification publique complète utilisable. Les résultats sur les décompilateurs d’exécutables, les exports réalisés avec le runtime, le format des fichiers de données et les transformations vers d’autres langages ne constituaient pas un lecteur autonome des fichiers binaires demandés.

## Travail sur le corpus

1. Inventaire des extensions, tailles et signatures. Conservation intacte de tous les originaux.
2. Comparaison des en-têtes des projets Origamide (31) et bridge (28), des fenêtres et états, des caches et des 24 générations historiques de l’analyse.
3. Détermination de la table à pas de cinq octets et du départ relatif du payload, puis vérification que la somme des sections correspond exactement à chaque fichier.
4. Essais de compression : absence de flux zlib valides ; détection d’enveloppes LZW, construction du décodeur et contrôle des longueurs.
5. Reconstruction du framing UTF-8 et des enregistrements ligne/auteur du WLangage, au lieu d’une extraction de chaînes imprimables.
6. Inspection **statique** de l’implémentation de lecture d’analyse dans la DLL fournie `wd310hf64.DLL`. Aucun chargement ou appel de la DLL, ni exécution de l’EXE. La bibliothèque distribuée ne contient aucun de ces composants.
7. Localisation des paramètres du lecteur hérité, de la dérivation de clé et de la transformation d’octets ; réimplémentation C# indépendante. Le checksum de tous les WDD décodés fournit une vérification qui dépasse la simple apparence de chaînes lisibles.
8. Reconstruction des répertoires de tables et rubriques ; comparaison sémantique avec `analysis.json`, utilisé uniquement comme oracle de test. Aucun code du lecteur n’importe ce JSON ni ne recherche le `.wda` voisin pour lire un `.wdd`.
9. Tests en .NET et sur les fichiers réels, cas négatifs, génération des DLL et du package.

Repères statiques pour reproduire l’inspection de **cette version précise** de `wd310hf64.DLL` (RVA, indépendants de l’adresse de chargement) : configuration du buffer d’analyse `0x4F85C8`, création de clé `0x50D6B0`, transformation inverse `0x50D410`, lecteur de l’en-tête du schéma `0x4F88A0`. Ces adresses ne sont jamais utilisées par la bibliothèque.

## Ce qui est établi et ce qui reste ouvert

Les structures documentées dans `FORMAT.md` sont contrôlées sur le corpus. Les types de contrôles inférés ne sont pas des structures certifiées. Le contenu de la section 1 des projets, les styles et hiérarchies graphiques, les règles de clés/relations et les variantes protégées nécessitent encore des spécimens et des comparaisons ciblées. L’API conserve les octets et affiche ces limites afin de permettre la poursuite de l’analyse sans perte d’information.

Une ouverture réussie signifie que le conteneur et les blocs pris en charge sont lisibles ; elle ne signifie pas que toutes les propriétés de l’éditeur WINDEV ont été comprises.
