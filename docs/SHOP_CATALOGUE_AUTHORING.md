# Expandable equipment shop

The shop console at the HQ depot's counter sells **all** entries in
`Assets/_Project/Resources/ShopCatalog.asset` (Dan, 8 October 2026: the console IS
the shop). The catalogue is painted on the console's top screen as rows of name,
price and state, three to a page with PREV / NEXT zones and the lever as NEXT PAGE;
aim at a row and press E to buy. There is no HUD browser and there are no item
stands any more.

## Add merchandise

1. Add a row in the catalogue Inspector. Give it a unique, stable ID, readable
   name, nonnegative price and kind. Keep existing IDs stable.
2. For a consumable, assign a working prefab with `NetworkObject` and the
   appropriate item components. Register that prefab in the project's FishNet
   spawnable prefab collection (`HQPrototypeLootSetup.PrefabObjectsPath`), just
   as `PatchKitSetup.Register()` does. Reuse existing item behaviour where suitable.
3. For an upgrade, use an implemented `PlayerUpgrade` effect. New effects require
   player/gameplay code and networking review; adding a name alone cannot create
   an effect. The current byte flags are not an unlimited upgrade system.
4. Validate HQ and build matching client/server revisions. A new catalogue row
   appears on the console (on its next page) without rebuilding the HQ. Clients
   must ship with the same catalogue and registered prefabs as their host.
5. Test purchase with a non-host client, insufficient shared funds, repeat upgrade
   purchase, and delivery/pickup. Prices, balance and eligibility are server-owned.

## Components and authority

- `HQShopConsole` (on the `Shop Console` root the `ShopConsoleBuilder` places): composes
  the screens from replicated state — `CrewDayState.ShopPage` (the shared page, a server
  SyncVar), the balance, the catalogue and the viewer's own owned bits — and `ShopConsolePaint`
  paints them. `ShopConsoleRules` (pure) pages the catalogue and words the lever.
- E on `Shop Row n` submits the existing `PlayerUpgrades.RequestBuy(id)` for the row's item;
  the server's refusal comes back to the buyer and the console shows it as a notice. E on
  `Shop Prev` / `Shop Next` asks `ShipControls.RequestShopPage(page)` (an absolute page);
  the lever's pull turns the page through the console lever path. See the contract's
  "The shop console" row (PROPOSED).
- `WorldSceneFlow.ServerBuy`: validates the catalogue ID, the buyer at the shop console
  (`ServerPresserAtRig(ConsoleKind.Shop)`), alive/travel/run state, funds and existing
  upgrade. It spends the crew pot and grants/spawns the purchase at the `ShopDeliveryPoint`
  nearest the console (the chute). No client supplies a price, delivery position, upgrade
  grant or balance.
- `ShopDisplay` and `ShopBrowserUI` stay compiled (old tests and the validator reference
  the types) but nothing in the scene places a display and nothing opens the browser.
- `HQPrototypeValidator`: refuses a `ShopDisplay` in the scene, requires the one Shop rig,
  and checks IDs, prices and prefab/upgrade configuration.

The HQ runtime check temporarily adds a fifth catalogue row using an already
registered consumable prefab, verifies it appears on the console's second page and
can be purchased for its price, then removes the test row. The real catalogue is
preserved. The `console-shop` and `shop` matrices check a separate executable
client's page and purchases at the console.

No stock limits, dynamic prices, unlocks or new upgrade effects are introduced.
