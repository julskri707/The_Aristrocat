1. Ersetze deine WorldPickupItem.cs mit dieser Version.

2. Zusätzliche Scripts importieren:
- PlayerPickupInteractor.cs
- PickupPromptUI.cs

3. Auf den Spieler:
- PlayerInventory
- PlayerPickupInteractor

Im Inspector von PlayerPickupInteractor:
- lookCamera = deine Spieler-Kamera
- playerInventory = dein PlayerInventory
- promptUI = dein PickupPromptUI
- interactDistance z. B. 4

4. UI bauen:
Unter Canvas:
- PickupPromptRoot
  - PromptText (TMP)

Auf PickupPromptRoot:
- PickupPromptUI

Im Inspector:
- root = PickupPromptRoot
- promptText = PromptText

PickupPromptRoot zuerst aktiv lassen, das Script blendet es selbst aus.

5. Auf jedes aufhebbare Objekt:
- Collider
- WorldPickupItem
- itemData setzen
- amount setzen

Wichtig:
Für Ansehen + Linksklick:
- requireTrigger = aus
- allowLookPickup = an

6. Ergebnis:
Wenn du das Objekt anschaust, erscheint:
- Linksklick um ... aufzuheben

Wenn du Linksklick drückst:
- Item wird aufgehoben
- kommt ins Inventar
