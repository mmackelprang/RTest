-- seed-test-history.sql
--
-- One-shot UAT fixture: inserts a few historical PlayHistory rows so the
-- /play-history page exercises all three branches of
-- Radio.Web.Formatting.Timestamps.FormatRelative visually during UAT.
--
-- The kiosk runs continuously and PlayHistory rows accrue only when a
-- track actually plays. In daily UAT every visible row tends to read
-- "Today HH:mm" because the freshly-played-through-the-day rows
-- dominate the LIMIT 20 window. This script seeds:
--
--   * One row from yesterday      -> exercises "Yesterday HH:mm" branch
--   * One row from 3 days ago     -> exercises "MMM d · HH:mm" branch
--   * One row from ~30 days ago   -> exercises "MMM d · HH:mm" branch
--                                    further back, to verify month rollover
--
-- Each row references a seeded TrackMetadata row so the JOIN in
-- SqlitePlayHistoryRepository.GetRecentAsync surfaces a real title/artist
-- instead of "Unknown - Unknown".
--
-- ## Usage
--
-- On the kiosk (Ubuntu x64) or Pi:
--
--   sqlite3 /opt/radio-console/data/fingerprints/fingerprints.db \
--           < deploy/scripts/seed-test-history.sql
--
-- Then open /play-history in the kiosk UI. You should see at minimum:
--
--   Today HH:mm        (existing rows from normal use, if any)
--   Yesterday HH:mm    UAT Seed - Yesterday's Broadcast / Tester Fixture
--   MMM d · HH:mm      UAT Seed - Three Days Back / Tester Fixture
--   MMM d · HH:mm      UAT Seed - One Month Back / Tester Fixture
--
-- ## Idempotency
--
-- All inserts use INSERT OR IGNORE keyed on stable Id GUIDs. Re-running
-- the script is a no-op after the first run. To re-seed with fresh
-- relative dates (e.g. after several weeks have passed and "yesterday"
-- is no longer yesterday), DELETE the rows by Id first, then re-run.
--
-- ## Cleanup
--
-- DELETE FROM PlayHistory  WHERE Id LIKE 'uat-seed-%';
-- DELETE FROM TrackMetadata WHERE Id LIKE 'uat-seed-%';

BEGIN TRANSACTION;

-- ---------------------------------------------------------------------------
-- Track metadata for the seeded rows. Realistic-ish titles so the UI doesn't
-- look like obvious test data on a casual glance, but tagged "UAT Seed -"
-- so an operator can spot and clean them.
-- ---------------------------------------------------------------------------

INSERT OR IGNORE INTO TrackMetadata (
  Id, FingerprintId, Title, Artist, Album, AlbumArtist,
  TrackNumber, DiscNumber, ReleaseYear, Genre,
  MusicBrainzArtistId, MusicBrainzReleaseId, MusicBrainzRecordingId, CoverArtUrl,
  Source, CreatedAt, UpdatedAt
) VALUES
  (
    'uat-seed-meta-yesterday',
    NULL,
    'UAT Seed - Yesterday''s Broadcast',
    'Tester Fixture',
    'Relative Time Tests',
    'Tester Fixture',
    NULL, NULL, NULL, NULL,
    NULL, NULL, NULL, NULL,
    'Manual',
    datetime('now', '-1 day'),
    datetime('now', '-1 day')
  ),
  (
    'uat-seed-meta-three-days',
    NULL,
    'UAT Seed - Three Days Back',
    'Tester Fixture',
    'Relative Time Tests',
    'Tester Fixture',
    NULL, NULL, NULL, NULL,
    NULL, NULL, NULL, NULL,
    'Manual',
    datetime('now', '-3 days'),
    datetime('now', '-3 days')
  ),
  (
    'uat-seed-meta-one-month',
    NULL,
    'UAT Seed - One Month Back',
    'Tester Fixture',
    'Relative Time Tests',
    'Tester Fixture',
    NULL, NULL, NULL, NULL,
    NULL, NULL, NULL, NULL,
    'Manual',
    datetime('now', '-30 days'),
    datetime('now', '-30 days')
  );

-- ---------------------------------------------------------------------------
-- Play history rows. Source values must match the PlaySource enum string
-- form (see src/Radio.Core/Models/Audio/PlayHistoryEntry.cs). PlayedAt is
-- ISO 8601; strftime emits the form SqlitePlayHistoryRepository expects
-- (DateTimeOffset.Parse round-trip).
-- ---------------------------------------------------------------------------

INSERT OR IGNORE INTO PlayHistory (
  Id, TrackMetadataId, FingerprintId,
  PlayedAt, EndedAt, Source, MetadataSource, SourceDetails,
  Duration, IdentificationConfidence, WasIdentified
) VALUES
  (
    'uat-seed-hist-yesterday',
    'uat-seed-meta-yesterday',
    NULL,
    strftime('%Y-%m-%dT%H:%M:%S.000+00:00', 'now', '-1 day'),
    strftime('%Y-%m-%dT%H:%M:%S.000+00:00', 'now', '-1 day', '+3 minutes'),
    'Radio',
    'Manual',
    'UAT seed (yesterday)',
    180,
    NULL,
    0
  ),
  (
    'uat-seed-hist-three-days',
    'uat-seed-meta-three-days',
    NULL,
    strftime('%Y-%m-%dT%H:%M:%S.000+00:00', 'now', '-3 days'),
    strftime('%Y-%m-%dT%H:%M:%S.000+00:00', 'now', '-3 days', '+3 minutes'),
    'File',
    'Manual',
    'UAT seed (three days ago)',
    195,
    NULL,
    0
  ),
  (
    'uat-seed-hist-one-month',
    'uat-seed-meta-one-month',
    NULL,
    strftime('%Y-%m-%dT%H:%M:%S.000+00:00', 'now', '-30 days'),
    strftime('%Y-%m-%dT%H:%M:%S.000+00:00', 'now', '-30 days', '+3 minutes'),
    'File',
    'Manual',
    'UAT seed (one month ago)',
    210,
    NULL,
    0
  );

COMMIT;
