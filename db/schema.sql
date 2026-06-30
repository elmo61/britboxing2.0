-- BritBoxing — Postgres / Supabase schema
-- Mirrors the JSON "fake DB" in ../data. JSONB holds the frozen snapshot shape
-- (per BOXING_SITE_PLAN.md); commonly-queried scalars are promoted to columns.
-- Run this first, then seed.sql. Idempotent: safe to re-run.

create table if not exists fighters (
    id              text primary key,          -- stable slug, e.g. 'anthony-joshua'
    name            text not null,
    dob             date,
    wikipedia_title text,
    has_wikipedia   boolean not null default false,
    -- promoted from the latest snapshot for easy querying / division pages
    wins            int,
    losses          int,
    draws           int,
    wins_ko         int,
    wins_dec        int,
    no_contests     int,
    age             int,
    stance          text,
    height_inches   int,
    reach_inches    int,
    weight_classes  text[],
    latest_snapshot jsonb,                      -- full latest snapshot
    updated_at      timestamptz not null default now()
);

-- Optional, per the plan — populated once promoter/venue data exists.
create table if not exists events (
    id         bigint generated always as identity primary key,
    title      text,
    slug       text unique,
    event_date date,
    venue      text,
    promoter   text,
    card_type  text
);

create table if not exists bouts (
    slug               text primary key,        -- '<fighterAId>-vs-<fighterBId>'
    fighter_a_id       text references fighters(id),
    fighter_b_id       text references fighters(id),
    weight_class       text,
    event_id           bigint references events(id),
    event_date         date,                    -- null = TBC
    announced_at       text,                    -- RSS publish date (free-form)
    headline           text,
    source             text,
    source_url         text,
    -- FROZEN at announcement time — never rewritten when a fighter updates
    fighter_a_snapshot jsonb not null,
    fighter_b_snapshot jsonb not null,
    prompt             jsonb,                   -- {system, user, model} for audit/regeneration
    created_at         timestamptz not null default now()
);

create table if not exists articles (
    slug         text primary key references bouts(slug) on delete cascade,
    title        text not null,
    summary      text,
    body         text not null,                 -- HTML
    tags         text[],
    ai_generated boolean not null default true,
    published_at timestamptz not null default now()
);

create index if not exists idx_bouts_fighter_a on bouts(fighter_a_id);
create index if not exists idx_bouts_fighter_b on bouts(fighter_b_id);
create index if not exists idx_fighters_weight  on fighters using gin (weight_classes);
