-- Reading-progress sync for the handbook reader (site/app.js).
-- Run once in the Supabase SQL editor. Safe to re-run.
--
-- Model: one row per sync code. The code is a random 128-bit secret generated in the
-- reader; whoever holds it can read and write that row, nobody can list or guess rows.
-- The table itself is locked (RLS on, no policies, no grants): the browser can only call
-- the two functions below with the public anon key. Merging happens in the browser;
-- the version column makes concurrent writes from two devices a retry, not a lost update.

create table if not exists public.sync_progress (
  key        text primary key check (char_length(key) between 20 and 64),
  data       jsonb  not null default '{}'::jsonb,
  version    bigint not null default 1,
  updated_at timestamptz not null default now()
);

alter table public.sync_progress enable row level security;
revoke all on public.sync_progress from anon, authenticated;

create or replace function public.sync_get(p_key text)
returns jsonb
language sql
stable
security definer
set search_path = ''
as $$
  select coalesce(
    (select jsonb_build_object('data', s.data, 'version', s.version)
       from public.sync_progress s where s.key = p_key),
    jsonb_build_object('data', '{}'::jsonb, 'version', 0));
$$;

-- p_version = 0 creates the row; otherwise the update applies only if the row is still at
-- p_version. On a conflict the current row comes back so the caller can merge and retry.
create or replace function public.sync_put(p_key text, p_data jsonb, p_version bigint)
returns jsonb
language plpgsql
security definer
set search_path = ''
as $$
declare
  new_version bigint;
begin
  if p_key is null or char_length(p_key) not between 20 and 64 then
    raise exception 'invalid key';
  end if;
  if p_data is null or jsonb_typeof(p_data) <> 'object' or octet_length(p_data::text) > 65536 then
    raise exception 'invalid data';
  end if;

  if p_version = 0 then
    insert into public.sync_progress (key, data) values (p_key, p_data)
      on conflict (key) do nothing;
    if found then
      return jsonb_build_object('ok', true, 'version', 1);
    end if;
  else
    update public.sync_progress
       set data = p_data, version = version + 1, updated_at = now()
     where key = p_key and version = p_version
    returning version into new_version;
    if found then
      return jsonb_build_object('ok', true, 'version', new_version);
    end if;
  end if;

  return jsonb_build_object('ok', false) || public.sync_get(p_key);
end;
$$;

revoke all on function public.sync_get(text) from public;
revoke all on function public.sync_put(text, jsonb, bigint) from public;
revoke execute on function public.sync_get(text) from authenticated;
revoke execute on function public.sync_put(text, jsonb, bigint) from authenticated;
grant execute on function public.sync_get(text) to anon;
grant execute on function public.sync_put(text, jsonb, bigint) to anon;
