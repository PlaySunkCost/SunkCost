# Expandable equipment shop

The equipment counter in the HQ depot sells **all** entries in
`Assets/_Project/Resources/ShopCatalog.asset`. Aim at the counter and press E.
Search, filters and the scrolling list are generated from that asset. The four
physical displays are optional featured samples, not capacity limits.

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
   appears at the counter without rebuilding the HQ or adding a stand. Clients
   must ship with the same catalogue and registered prefabs as their host.
5. Test purchase with a non-host client, insufficient shared funds, repeat upgrade
   purchase, and delivery/pickup. Prices, balance and eligibility are server-owned.

## Components and authority

- `ShopDisplay.BrowsesCatalog`: a physical counter offering the complete catalogue.
  `ConfigureCatalog(delivery)` wires it to the existing chute. Ordinary displays
  may still feature a single item through `Configure(id, label, delivery)`.
- `ShopBrowserUI`: local owner interface; search, category filter and scrolling.
  It submits the existing `PlayerUpgrades.RequestBuy(id)` and shows server refusals.
- `SessionInputGate.ShopOpen`: releases the mouse and blocks gameplay commands;
  Escape closes the browser and suppresses the closing click.
- `WorldSceneFlow.ServerBuy`: validates the catalogue ID and the nearest eligible
  counter/display in HQ, range, alive/travel/run state, funds and existing upgrade.
  It spends the crew pot and grants/spawns the purchase. No client supplies a price,
  delivery position, upgrade grant or balance. There is no new RPC or SyncVar.
- `HQPrototypeValidator`: accepts a catalogue counter instead of demanding one
  stand per item, and checks IDs, prices and prefab/upgrade configuration.

The HQ runtime check temporarily adds a fifth catalogue row using an already
registered consumable prefab, verifies it appears and can be purchased without
a dedicated display, then removes the test row. The real catalogue is preserved.
The shop matrix checks a separate executable client's browser and purchases.

No stock limits, dynamic prices, unlocks or new upgrade effects are introduced.
