# BritBoxing

Automated UK boxing news / fight-preview site. See `BOXING_SITE_PLAN.md` for the
full architecture and `CLAUDE.md` for decisions layered on top.

## Repository layout

```
britboxing2.0/
├── backend/              # C# (.NET 8) content pipeline — the whole engine
│   └── src/
│       ├── BritBoxingFeeds.Core/    all logic: Sources (RSS), Extraction (regex + Claude),
│       │                            Deduplication, State (seen items), Enrichment (Wikipedia
│       │                            snapshots), Fighters, Processing (decide→bout→article),
│       │                            Articles (generation), Supabase client, Deploy trigger
│       └── BritBoxingFeeds.App/     thin console runner (a future API can sit beside it)
├── web/                  # Nuxt 3 site; Nitro API reads Supabase, prerendered to static HTML
│   ├── server/api/       /api/fights + /api/fighters
│   ├── pages/            index, fights/[slug], fighters/ + fighters/[id]
│   └── components/FightCard.vue
├── db/                   # Supabase/Postgres schema, seed, RLS policies
├── data/                 # historical JSON content from the POC era (seeded Supabase; kept for reference)
├── .github/workflows/pipeline.yml   # runs the pipeline every 3 hours
├── BOXING_SITE_PLAN.md  CLAUDE.md
```

## How it works

1. **`backend/`** polls boxing RSS feeds every 3 hours (GitHub Actions), skips
   items it has already seen (`seen_feed_items` in Supabase), extracts fight
   details (regex first, Claude for anything the regex can't fill), decides
   whether each fight deserves a preview, freezes both fighters' Wikipedia
   records into a bout, generates the article, and writes it all to Supabase.
2. **`web/`** (Nuxt) is a static site on Render, rebuilt from Supabase content.
   Code pushes auto-deploy; the pipeline POSTs a deploy hook when it publishes
   new content.

## Run it locally

```bash
# the pipeline (needs backend/.env with ANTHROPIC_API_KEY, SUPABASE_URL,
# SUPABASE_SECRET_KEY, RENDER_DEPLOY_HOOK_URL)
cd backend
dotnet run --project src/BritBoxingFeeds.App              # human-readable report
dotnet run --project src/BritBoxingFeeds.App -- --json    # JSON to stdout

# the site (needs web/.env with the publishable Supabase pair)
cd web
npm install
npm run dev                     # http://localhost:3000
```

The Python `pipeline/` that built the original POC was retired on 2026-07-02
once the C# port reached parity — it lives on in git history.
