# teams-app

Teams app package (manifest + icons) for sideloading the Mahdar bot.

## Before packaging
Replace the placeholders in `manifest.json`:
- `REPLACE_WITH_BOT_APP_ID` (3 places) → the Entra app / Azure Bot id (`BOT_APP_ID`).
- `REPLACE_WITH_PUBLIC_HOSTNAME` → the media bot's public FQDN.

`color.png` (192×192) and `outline.png` (32×32) are placeholder icons — swap for real
branding before any non-dev use.

## Package + sideload
```bash
zip -j mahdar-teams-app.zip manifest.json color.png outline.png
```
Upload via Teams Admin Center → Manage apps → Upload, or the Teams client → Apps →
"Manage your apps" → "Upload a custom app". Requires custom app upload enabled
(see `docs/SETUP-AZURE.md`).

MVP activation is via `scripts/join.py` / `POST /api/joinCall` — there is no in-meeting UI.
