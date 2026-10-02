===================================================================
 CIMP — Coût d'Importation Algérie
 Version 1.0.0
===================================================================

CIMP calcule le coût de revient réel des marchandises importées en Algérie :
valeur en douane, droits de douane, taxes (TVA, DAPS, PRCT, RPS...), frais
d'approche, et coût de revient / prix de vente / bénéfice par article.

-------------------------------------------------------------------
CONFIGURATION REQUISE
-------------------------------------------------------------------
- Windows 10 ou Windows 11, 64 bits.
- Aucune installation de .NET n'est nécessaire : cette version est
  autonome ("self-contained"), tout ce qu'il faut pour exécuter CIMP
  est déjà inclus dans le dossier.
- Environ 200 Mo d'espace disque libre.

-------------------------------------------------------------------
INSTALLATION
-------------------------------------------------------------------
1. Téléchargez le fichier CIMP-1.0.0-Windows-x64.zip.
2. Faites un clic droit sur le fichier ZIP puis "Extraire tout...".
3. Ouvrez le dossier extrait "CIMP-1.0.0-Windows-x64".
4. Double-cliquez sur CIMP.exe pour lancer l'application.

Aucune autre installation (pas de Visual Studio, pas de .NET, pas de
base de données séparée à installer) n'est nécessaire.

-------------------------------------------------------------------
PREMIER DÉMARRAGE
-------------------------------------------------------------------
Au tout premier lancement, CIMP crée automatiquement sa base de données
locale dans :
    %LOCALAPPDATA%\CIMP\cimp.db
(c'est-à-dire, en général : C:\Utilisateurs\<votre nom>\AppData\Local\CIMP\cimp.db)

Un compte administrateur technique "admin" est créé automatiquement avec
un mot de passe généré aléatoirement, affiché UNE SEULE FOIS à l'écran de
connexion. Notez-le immédiatement : l'application vous demandera de le
changer dès la première connexion.

-------------------------------------------------------------------
DÉPANNAGE
-------------------------------------------------------------------

1) "Windows a protégé votre ordinateur" (SmartScreen) au lancement :
   Ce message apparaît pour tout nouvel exécutable qui n'est pas encore
   connu de Microsoft ou qui n'est pas signé numériquement. Cliquez sur
   "Informations complémentaires" puis "Exécuter quand même".

2) Le ZIP semble "bloqué" par Windows après téléchargement :
   Clic droit sur le fichier ZIP → Propriétés → cocher "Débloquer" en bas
   de la fenêtre → OK, puis extraire à nouveau.

3) Rien ne se passe en double-cliquant sur CIMP.exe :
   - Vérifiez que vous avez bien EXTRAIT le ZIP avant de lancer CIMP.exe
     (ne pas l'exécuter directement depuis l'intérieur du ZIP).
   - Vérifiez que tous les fichiers du dossier sont présents (ne déplacez
     jamais CIMP.exe seul en dehors de son dossier : il a besoin des
     autres fichiers à côté de lui pour fonctionner).

4) Message d'erreur au démarrage concernant un antivirus :
   Certains antivirus peuvent mettre en quarantaine un nouvel exécutable
   par précaution. Ajoutez une exception pour le dossier CIMP si cela se
   produit, après vous être assuré que le fichier ZIP provient bien de la
   source officielle (vérifiez le SHA-256 fourni avec le ZIP — voir
   SHA256.txt).

5) J'ai oublié le mot de passe affiché au premier démarrage :
   Contactez votre administrateur CIMP ; il n'existe volontairement aucun
   mot de passe "universel" caché dans le logiciel.

6) L'application affiche "INFORMATION NON DÉTERMINÉE" pour un taux ou une
   règle douanière :
   Ce n'est pas une anomalie technique : CIMP refuse délibérément de
   deviner un taux de change, un taux de TVA ou un droit de douane qui
   n'a pas été renseigné par un utilisateur habilité. Renseignez la
   valeur manquante dans l'écran "Taux de change" ou "Réglementation".

7) Je veux vérifier que le fichier téléchargé n'a pas été modifié :
   Calculez le SHA-256 du fichier ZIP (clic droit → propriétés selon
   votre utilitaire, ou "Get-FileHash" en PowerShell) et comparez-le à
   la valeur indiquée dans SHA256.txt fourni à côté du ZIP.

-------------------------------------------------------------------
DÉSINSTALLATION
-------------------------------------------------------------------
- Version ZIP : supprimez simplement le dossier CIMP-1.0.0-Windows-x64
  (et, si vous souhaitez aussi effacer vos données, le dossier
  %LOCALAPPDATA%\CIMP).
- Version installateur (CIMP-1.0.0-Setup-x64.exe) : utilisez
  "Applications" dans les paramètres Windows, ou le raccourci de
  désinstallation créé dans le menu Démarrer.

-------------------------------------------------------------------
SUPPORT
-------------------------------------------------------------------
Pour toute question ou anomalie, contactez l'équipe CIMP en indiquant :
- la version de CIMP utilisée (voir menu "? → À propos") ;
- la version de Windows ;
- une description précise de l'action qui a provoqué le problème.
