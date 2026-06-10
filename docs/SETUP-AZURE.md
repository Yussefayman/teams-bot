# SETUP-AZURE.md — M0 Foundations Runbook

Manual Azure / Microsoft 365 setup. None of this can be scripted from a dev Mac (it
needs a Microsoft account, the Azure portal, and admin consent). Do the steps in order
and record the values into `media-bot/.env` (never commit secrets).

## 0. Accounts

- [ ] Join the **Microsoft 365 Developer Program** → instant free dev tenant with admin
      rights. Provision the sample data pack so you get test users.
- [ ] Create at least **3 test users** (for multi-participant meeting tests).
- [ ] An **Azure subscription** (the dev tenant's, or a pay-as-you-go) for the Bot + VM.

## 1. Enable custom app sideloading

- [ ] Teams Admin Center → **Teams apps → Setup policies → Global** → enable
      **"Upload custom apps"**. (Allows sideloading the `teams-app/` package later.)

## 2. Entra ID app registration (one app for everything)

- [ ] Entra ID → **App registrations → New registration**. Record **`TENANT_ID`**,
      **`CLIENT_ID`** (= `BOT_APP_ID`).
- [ ] **Certificates & secrets → New client secret** → record **`CLIENT_SECRET`**
      (= `BOT_APP_SECRET`).
- [ ] **API permissions → add (Application permissions)** then **Grant admin consent**:
  - `Calls.AccessMedia.All`  ← required for application-hosted media (raw audio)
  - `Calls.JoinGroupCall.All`
  - `Calls.JoinGroupCallAsGuest.All`
  - `Calls.Initiate.All`
  - `OnlineMeetings.Read.All`
  - `Chat.ReadWrite.All`
  - `Calendars.ReadWrite`
  - `User.Read.All`

## 3. Azure Bot resource + Teams calling

- [ ] Create an **Azure Bot** resource; link it to the app registration above
      (Microsoft App ID = `CLIENT_ID`).
- [ ] **Channels → Microsoft Teams** → add. In Teams channel **Calling** settings, enable
      calling and set the webhook to `https://<PUBLIC_HOSTNAME>/api/calling`.

## 4. Windows VM for the media bot

Application-hosted media is Windows-only (cannot run in App Service/containers/Linux).

- [ ] Provision a **Windows Server VM** (Standard D4s v5 to start).
- [ ] Assign a **public DNS name** → this is `PUBLIC_HOSTNAME`.
- [ ] Obtain a **TLS certificate** for that FQDN (real domain + Let's Encrypt or
      Azure-issued). Import to the machine cert store; record the **thumbprint**
      (`CERT_THUMBPRINT`).
- [ ] Bind the cert to the media port:
      `netsh http add sslcert ipport=0.0.0.0:8445 certhash=<THUMBPRINT> appid={<guid>}`
- [ ] Open ports in the **NSG + Windows firewall**: `443` (signaling/HTTP) and the media
      TCP port (`MEDIA_PORT`, default `8445`).
- [ ] Install the **.NET 8 runtime** on the VM.

## 5. Deploy the media bot to the VM

- [ ] `git clone` the repo on the VM.
- [ ] Set env vars (`CALL_SOURCE=graph`, `BOT_APP_ID`, `BOT_APP_SECRET`, `TENANT_ID`,
      `PUBLIC_HOSTNAME`, `MEDIA_PORT`, `CERT_THUMBPRINT`, `STT_WS_BASE_URL`,
      `ORCHESTRATOR_BASE_URL`, `DUMP_AUDIO=true`).
- [ ] `media-bot\deploy.ps1` → publishes `MediaBot.Host` (Windows build pulls in
      `MediaBot.Graph` + the Graph Communications SDK).
- [ ] Finish the `TODO(graph-sdk)` items in `MediaBot.Graph/GraphCallSource.cs` against
      the pinned RecordingBot SDK version, then rebuild.

## 6. STT + orchestrator hosts (reachable from the VM)

- [ ] STT service on a GPU host (Linux), reachable at `STT_WS_BASE_URL` (use a tunnel in
      dev). Orchestrator anywhere reachable at `ORCHESTRATOR_BASE_URL`.

---

When all boxes are checked, M0 is done. Proceed to **M1**: run the bot with
`CALL_SOURCE=graph`, join a dev-tenant meeting, and confirm `tools/check_wav.py` reports
`CONFORMS` on the dumped WAV (see `docs/M0-M1-CHECKLIST.md`).
