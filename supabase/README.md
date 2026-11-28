# Reading-progress sync (Supabase)

Optional backend for the reader's cross-device progress sync. The reader works without it
(progress stays in each browser); with it, the **Aa** panel gets a *Sync progress between
devices* row.

## How it works

- Type a **nickname** in the Aa panel and press **Connect**. If the nickname exists, the device
  connects and merges; if not, the reader asks *Create it?* first (so a typo does not silently
  start a new, empty account). Use the same nickname on every device.
- **There is no password, on purpose.** The nickname is the key of one row (`u:<nick>`); anyone
  who types it sees and writes that progress, and the server confirms whether a nickname exists.
  That is acceptable for reading progress only. Do not put anything else behind this scheme.
- Only per-chapter progress (`pos:*`: percent, finished, resume block, timestamp) is stored.
  Language, theme, What's New marks and everything else stay local.
- Merging is done in the browser: the larger percentage wins, *finished* is sticky, and a reset
  is a dated zero record that beats older progress. `sync_put` compares a version number, so two
  devices writing at once retry and merge again instead of overwriting each other.
- The table is locked (RLS on, no policies, no grants). The browser holds only the public
  anon/publishable key and can call just `sync_get` and `sync_put` (`schema.sql`; keys of 3–64
  characters, payloads up to 64 KB). Nobody can list rows.

## Set up your own project

1. Create a free project at supabase.com.
2. SQL editor → run `supabase/schema.sql` (safe to re-run).
3. Project Settings → API → copy the **Project URL** and the **publishable** (or legacy anon) key
   into `site/sync-config.js`. Never use the `service_role` key.
4. Leave both fields empty to switch the feature off.

## Free-tier notes

- Projects pause after 7 days without activity; `.github/workflows/supabase-keepalive.yml` makes
  one read every three days. GitHub disables scheduled workflows after 60 days without repo
  activity; if the project did pause, restore it from the Supabase dashboard (data is kept).
- Each device's progress is a few KB, so storage is a non-issue.
- The anon key is public by design. If someone floods the endpoint, rotate the key (Settings →
  API) and update `site/sync-config.js`.
