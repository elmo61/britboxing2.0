# Database (Supabase / Postgres)

A lightweight Postgres schema for the data currently held as JSON in `../data`.
The JSON files stay the **source of truth** (the pipeline writes them); this
folder turns them into SQL you can run on a free hosted Postgres.

```
db/
├── schema.sql       tables: fighters, bouts, articles, events  (JSONB snapshots)
├── export_sql.py    reads ../data and writes seed.sql
├── seed.sql         generated — INSERT ... ON CONFLICT for every fighter/bout/article
└── README.md
```

## Model

- **fighters** — one row per boxer (stable id, e.g. `anthony-joshua`). Commonly
  queried scalars (wins, KO, reach, age, weight classes) are promoted to columns;
  the full latest snapshot is kept in `latest_snapshot jsonb`.
- **bouts** — one row per matchup (`<fighterAId>-vs-<fighterBId>`). Holds the two
  **frozen** snapshots as `jsonb` (historically accurate forever), plus the prompt
  used to write the article.
- **articles** — the generated preview copy (FK to its bout).
- **events** — optional, per the plan; populate when promoter/venue data exists.

## Host it free on Supabase

1. Create a project at supabase.com (free tier ~500MB).
2. In the dashboard: **SQL Editor → New query**, paste `schema.sql`, run.
3. New query, paste `seed.sql`, run. Done — 18 fighters / 9 bouts / 9 articles.

Or from a terminal with the project's connection string (Project Settings →
Database → Connection string):

```bash
psql "$DATABASE_URL" -f db/schema.sql
psql "$DATABASE_URL" -f db/seed.sql
```

## Regenerate the seed after the data changes

```bash
python db/export_sql.py     # rewrites db/seed.sql from ../data
```

`seed.sql` uses `ON CONFLICT ... DO UPDATE`, so re-running it is an upsert — safe
to apply repeatedly as the data evolves.

## Validate locally (no account needed)

```bash
docker run -d --name bb_pg -e POSTGRES_PASSWORD=postgres postgres:16-alpine
docker exec -i bb_pg psql -U postgres -v ON_ERROR_STOP=1 < db/schema.sql
docker exec -i bb_pg psql -U postgres -v ON_ERROR_STOP=1 < db/seed.sql
docker exec -it bb_pg psql -U postgres        # poke around
docker rm -f bb_pg                            # tear down
```

## How it connects

- **Site reads** from Supabase: the Nuxt Nitro API (`web/server/`) queries
  Postgres with the publishable key (read-only RLS via `policies.sql`).
- **Pipeline writes** to Supabase: `pipeline/supabase_db.py` upserts with the
  secret key (`python pipeline.py push`, or write-through on discover/samples).
- The JSON in `../data` remains the source of truth; `seed.sql` / `push` are two
  ways to load it into the DB.
