# The port leak — 18 September 2026 — two manual checks, both pass

Dan: "I can't play — port taken." The Unity editor held UDP 7770 after a
Play Mode that had hosted; only a restart freed it.

## Fixed
- `PlayModeSessionGuard` (editor): on ExitingPlayMode, if the session
  controller is in a room, `Leave()` first — the same Leave the menu button
  and StopCleanly use — so the transport closes its socket before the domain
  reload.
- `PrototypeSessionController`: a Local host takes the first free UDP port
  from the configured one (`FreeUdpPort`, moved here from the matrix driver)
  and logs "UDP 7770 is taken; hosting on 7771 instead"; the room message
  names the port actually bound (`LocalPort`).

## Checks
1. Hosted in the editor (the `smooth` matrix), confirmed 7770 held by Unity,
   then a **bare `ExitPlaymode`** (the command file's `exit-play`, not the
   clean stop): 8 s later **7770 free**. Before the fix this was the leak.
2. Held 7770 with a PowerShell `UdpClient`, launched the development build
   with `-hq-auto-host-local`: its log says
   `[Session] UDP 7770 is taken; hosting on 7771 instead.` and 7771 was
   bound by the build while 7770 stayed with PowerShell.
