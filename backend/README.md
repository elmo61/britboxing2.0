# BritBoxingFeeds (C#)

The C# backend that runs the whole content pipeline: pulls fight announcements
from British boxing news feeds, extracts structured fields, dedupes across
sources, skips already-seen items, verifies fighters against Wikipedia, creates
bouts with frozen record snapshots, generates the preview articles, writes it
all to Supabase and triggers a rebuild of the static site. Replaced the
original Python `pipeline/` (retired 2026-07-02; in git history).

## Structure

```
src/
  BritBoxingFeeds.Core/            all logic, one library:
    Models/                        FightAnnouncement
    Interfaces/ + FightAggregator  IFightSource fan-out
    Sources/                       RssFightSourceBase + BBC / BoxingScene / WorldBoxingNews /
                                   YouTube (+ MatchroomSource, not registered: promoter
                                   scraping is out of scope per ../CLAUDE.md)
    Extraction/                    Regex (free) + Anthropic (LLM) + Composite (regex-first)
    Deduplication/                 merge the same fight across sources (name-pair + date tolerance)
    State/                         SeenFeedItemsStore — skip items processed in prior runs;
                                   status lifecycle new/ignored/bout_created/article_created
    Enrichment/                    WikipediaSnapshotService — MediaWiki -> JSONB fighter snapshot
    Fighters/                      FighterStore — stable slug IDs, fighters table upserts
    Processing/                    BoutProcessor — decide -> bout -> article orchestration
    Articles/                      ArticleGenerator — house-style preview via Claude
    Supabase/                      thin PostgREST client (service-role key)
    Deploy/                        SiteDeployTrigger — POST the Render deploy hook
  BritBoxingFeeds.App/             console runner (DI wiring); an API project could sit
                                   beside it sharing Core
```

## Run

Requires the .NET 8 SDK and a `backend/.env` (gitignored) with
`ANTHROPIC_API_KEY`, `SUPABASE_URL`, `SUPABASE_SECRET_KEY`,
`RENDER_DEPLOY_HOOK_URL`.

```bash
cd backend
dotnet run --project src/BritBoxingFeeds.App             # full pipeline + report
dotnet run --project src/BritBoxingFeeds.App -- --json   # raw JSON to stdout
```

The app fails fast if Supabase is unreachable (nothing downstream could be
persisted). Without `ANTHROPIC_API_KEY` extraction runs regex-only.

- **`NuGet.config`** pins restore to nuget.org (`<clear/>` drops any inherited
  private/enterprise feeds that would 401 on a personal build).
- In production this runs on a GitHub Actions schedule every 3 hours
  (`.github/workflows/pipeline.yml`); secrets live in the repo's Actions
  settings.

## Known issues

- BoxingScene's `/rss` returns 403 (dead feed) — fix the URL or drop it.
- The Python `style_check.py` AI-tells lint was not ported; the rules live in
  the generation prompt but there's no post-generation validation pass yet.
