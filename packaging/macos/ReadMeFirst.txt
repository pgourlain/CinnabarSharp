CinnabarSharp - first launch on macOS
======================================

1. Drag CinnabarSharp.app onto the Applications folder.

2. CinnabarSharp is not notarized by Apple, so macOS blocks it the first time:
   - Double-click CinnabarSharp in Applications. A warning appears: click "Done".
   - Open System Settings > Privacy & Security, scroll down to Security,
     and click "Open Anyway" next to "CinnabarSharp was blocked".
   - Confirm with your password, then click "Open".
   You only do this once.

   Or, in Terminal:
     xattr -dr com.apple.quarantine /Applications/CinnabarSharp.app

---

CinnabarSharp - premier lancement sur macOS
===========================================

1. Glissez CinnabarSharp.app dans le dossier Applications.

2. CinnabarSharp n'est pas notarise par Apple : macOS le bloque au premier lancement.
   - Double-cliquez sur CinnabarSharp dans Applications. Un avertissement s'affiche : cliquez sur "Terminer".
   - Ouvrez Reglages Systeme > Confidentialite et securite, descendez jusqu'a Securite,
     puis cliquez sur "Ouvrir quand meme" a cote de "CinnabarSharp a ete bloque".
   - Confirmez avec votre mot de passe, puis cliquez sur "Ouvrir".
   Il suffit de le faire une fois.

   Ou, dans le Terminal :
     xattr -dr com.apple.quarantine /Applications/CinnabarSharp.app
