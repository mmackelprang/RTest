# Radio Console documentation

These are the current, maintained docs. Historical plans, handoffs and UAT records are in
[`../archive/`](../archive/), which is not maintained.

## Reference

- [architecture.md](architecture.md): layers, the audio pipeline, and dataflow and latency.
- [configuration.md](configuration.md): configuration options, preferences, secrets and setup.
- [configuration-databases.md](configuration-databases.md): SQLite database paths and unified backup.
- [appsettings.example.json](appsettings.example.json): an annotated example configuration.
- [api.md](api.md): a REST and SignalR guide. The live reference is Scalar at `/scalar/v1`.
- [deployment.md](deployment.md): the appliance, services, kiosk, deploy script and verification.
- [integrations.md](integrations.md): rotary encoders, phone notifications and the announcement API.
- [testing.md](testing.md): test projects, conventions and seams.
- [fingerprinting.md](fingerprinting.md): song recognition (SongRec), metadata and album art.
- [metrics.md](metrics.md): metrics collection, rollups and the dashboard.
- [rtl-sdr-debugging.md](rtl-sdr-debugging.md): RTL-SDR hardware debugging.
- [direct-cast-channel.md](direct-cast-channel.md): the direct Cast-channel streaming mode.
- [direct-channel-setup-guide.md](direct-channel-setup-guide.md): setting up the custom Cast receiver.
- [hardware/](hardware/): front-panel drawings and engraving files.

## Decisions

- [decisions/DECISION-LOG.md](decisions/DECISION-LOG.md): the architectural decision log (ADR-001 onward).
- [decisions/](decisions/): standalone ADRs and decision records.

## Work tracking

- [known-issues-and-future-work.md](known-issues-and-future-work.md): open items at v1.0.0, and stubbed
  features.
- [BUILDER_QUEUE.md](BUILDER_QUEUE.md): the live work queue. Each row's detail is in `queue/<ID>.md`.
- [queue/ORDERING-NOTES.md](queue/ORDERING-NOTES.md), [queue/FAST-FOLLOWS.md](queue/FAST-FOLLOWS.md),
  [queue/CROSS-REPO-HANDOFFS.md](queue/CROSS-REPO-HANDOFFS.md) and `queue/inbound/`: the queue's process notes.

## Published files

[`receiver.html`](receiver.html) and [`receiver-direct-channel.html`](receiver-direct-channel.html) are the
Google Cast receiver pages. They are published to GitHub Pages from this folder by
`.github/workflows/pages-docs.yml`, which uploads all of `docs/`. Do not move or rename them.
