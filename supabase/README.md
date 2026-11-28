# Reading-progress sync (Supabase)

Optional backend for the reader's cross-device progress sync. The reader works without it
(progress stays in each browser); with it, the **Aa** panel gets a *Sync progress between
devices* row.

## How it works

- **Turn on sync** generates a random 128-bit **sync code** and uploads the progress. On another
  device choose **Connect** and enter the code. Anyone holding the code can read and write that
  one row; nobody can list or guess rows.
- Only per-chapter progress (`pos:*`: percent, finished, resume block, timestamp) is stored.
  Language, theme, What's New marks and everything else stay local.
- Merging is done in the browser: the larger percentage wins, *finished* is sticky, and a reset
  is a dated zero record that beats older progress. `sync_put` compares a version number, so two
  devices writing at once retry and merge again instead of overwriting each other.
- The table is locked (RLS on, no policies, no grants). The browser holds only the public
  anon/publishable key and can call just `sync_get` and `sync_put` (`schema.sql`).
  Payloads over 64 KB and keys outside 20–64 characters are rejected.

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
