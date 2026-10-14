# WINDEV Reverse Engineering

> Avertissement : Ce projet et le travail de reverse-engineering et d'analyse des fichiers binaires ont été réalisés par IA.

WINDEV-RE est une documentation présentant les principaux formats propriétaires de WINDEV et leur structure binaire devinée.

Elle peut servir de base de connaissance non négligeable pour comprendre les formats et les lire en dehors de l'écosystème WINDEV. La bibliothèque C# WindevReader, elle aussi réalisée par l'IA, accompagne cette documentation pour permettre la lecture des différents formats par un programme C#.

Les formats sont tous binaires, et sont d'une complexité plus que notable : entre du chiffrement, de la compression via un algorithme propriétaire, du XOR, etc. Cette complexité est probablement volontaire pour dissuader l'utilisation d'un autre outil et surtout de verrouiller le développeur dans l'écosystème.

Cette expérience vise à mieux comprendre les formats de cette technologie pour que le code source d'un développeur **puisse rester le sien**.