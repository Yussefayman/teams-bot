# M0 + M1 — what's done vs. what needs your hands

## Done in this repo (built + tested, runs on macOS)

### M0 — Foundations (the buildable parts)
- [x] Monorepo scaffolded per plan §2 (`media-bot`, `stt-service`, `orchestrator`,
      `teams-app`, `shared/schemas`, `docs`, `tools`, `scripts`).
- [x] Three shared JSON schemas written **and validated** against valid + invalid
      fixtures — `tools/validate_schemas.py` (7/7 expectations pass).
- [x] Config templated via env vars; per-service `.env.template`; zero hardcoded values.
- [x] Python `.venv` with dev/test tooling; service `requirements.txt` declared.

### M1 — Audio proof (everything except the live Teams socket)
- [x] `WavWriter` producing the exact PCM 16k/16-bit/mono RIFF contract
      (`media-bot/AUDIO-FORMAT.md`); **8 xUnit unit tests pass**.
- [x] Portable WAV oracle `tools/check_wav.py` + self-test (proves the pass/fail check).
- [x] Full media-bot pipeline runs **on this Mac** via `FakeCallSource`:
      `dotnet run` → `POST /api/joinCall` → STT WebSocket received every PCM byte
      (nothing lost), `session_start`/`session_end` delivered, **`DUMP_AUDIO` WAV
      CONFORMS**.
- [x] End-to-end xUnit integration test of that pipeline (9th test) passes.
- [x] `MediaBot.Host` + `MediaBot.Core` build and run cross-platform; `MediaBot.Graph`
      isolated as the only Windows-only project.

### Verify it yourself
```bash
tools/verify.sh
export PATH="$HOME/.dotnet:$PATH" && dotnet test media-bot/tests/MediaBot.Tests.csproj
```

## Needs your hands (cannot run from a Mac — account/portal/Windows)

### M0 — Azure/M365 (see docs/SETUP-AZURE.md)
- [ ] M365 Developer tenant + 3 test users.
- [ ] Enable custom app sideloading.
- [ ] Entra app registration + client secret + **admin consent** on the 8 app permissions.
- [ ] Azure Bot resource + Teams **Calling** channel → `/api/calling` webhook.
- [ ] Windows VM + public DNS + **TLS cert bound to the media port** + firewall/NSG ports.

### M1 — Real-meeting audio proof
- [ ] Finish the `TODO(graph-sdk)` in `MediaBot.Graph/GraphCallSource.cs` against the
      pinned `microsoft/graph-comms-samples` RecordingBot SDK version.
- [ ] On the VM: `CALL_SOURCE=graph`, run `deploy.ps1`, `POST /api/joinCall` with a real
      dev-tenant join URL.
- [ ] Bot appears as a participant within ~10s; let it run 5 minutes.
- [ ] **Exit criterion:** `python3 tools/check_wav.py dump_<id>.wav` → `CONFORMS`, and the
      WAV plays back as clear meeting audio.

> The Mac pipeline already proves the audio plumbing, lifecycle, buffering, and dump are
> correct. The only unproven piece is the SDK's mixed-audio-socket wiring, which is
> inherently Windows-only — that's the single remaining M1 risk.
