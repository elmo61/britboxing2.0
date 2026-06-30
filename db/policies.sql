-- Public read-only access for the site (anon / publishable key).
-- The data is public content, so allow SELECT to everyone and nothing else.
-- No INSERT/UPDATE/DELETE policy = writes are blocked for the public key;
-- the pipeline writes with a privileged key / direct connection instead.
-- Idempotent: safe to re-run.

alter table fighters enable row level security;
alter table bouts    enable row level security;
alter table articles enable row level security;
alter table events   enable row level security;

drop policy if exists "public read fighters" on fighters;
drop policy if exists "public read bouts"    on bouts;
drop policy if exists "public read articles" on articles;
drop policy if exists "public read events"   on events;

create policy "public read fighters" on fighters for select using (true);
create policy "public read bouts"    on bouts    for select using (true);
create policy "public read articles" on articles for select using (true);
create policy "public read events"   on events   for select using (true);
