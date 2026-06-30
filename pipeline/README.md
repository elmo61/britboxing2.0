# BritBoxing content pipeline

Python pipeline that turns a bout into the JSON the Nuxt site reads. It writes to
`../data` (`packages/`, `prompts/`, `articles/`). Rendering lives in the Nuxt app
(`../web`), not here.

```
feeds.py     poll BBC/ESPN/Sky boxing RSS into raw items              (FeedIngestionService)
detect.py    AI: is this item a fight announcement? + extract names   (part of FeedIngestionService)
snapshots.py Wikipedia -> JSONB snapshot, reuses ../britboxing_wikifetch (FighterEnrichmentService)
fighters.py  fighters DB: stable ids, upsert, bout backlinks          (Fighter model + dedup)
article.py   build the Claude prompt; call the API when a key is set   (ArticleGenerationService)
pipeline.py  orchestrator                                             (PipelineJob)
style_check  flags "AI tell" patterns in copy
```

## Detection, dedup, missing data

`detect.py` decides if an RSS item is a fight announcement: it calls Claude (cheap
`claude-haiku-4-5` classifier, structured JSON) when `ANTHROPIC_API_KEY` is set,
and falls back to the `feeds.py` regex otherwise. AI confirmation also lets a
star-vs-unknown bout through (the unknown gets a **sparse** name-only snapshot
via `snapshots.build_snapshot(name, allow_sparse=True)`); the regex path stays
strict (both must resolve on Wikipedia) to keep noise out.

A bout's slug is `<fighterAId>-vs-<fighterBId>`, so the same matchup is always the
same file — `discover` skips matchups already on disk (dedup) and moves to the
next. `fighters.py` gives each boxer a stable id (name slug, disambiguated by
birth-year on a genuine clash), stores their latest snapshot, and appends a
`bouts` backlink — that backlink is what the Nuxt fighter pages use for internal
links. Bouts also carry `eventDate` (nullable / TBC) and `announcedAt`.

Each module maps onto a service in `BOXING_SITE_PLAN.md`, so porting to C# is a
direct translation.

## Run

```bash
pip install requests mwparserfromhell feedparser anthropic
python pipeline.py discover     # poll feeds, snapshot first VALID bout, build prompt
python pipeline.py samples      # snapshot every pair in sample_matchups.json
python pipeline.py lint         # flag AI-tell patterns across all articles
python pipeline.py push         # bulk-sync data/ to Supabase
```

## Writing to Supabase

`supabase_db.py` upserts fighters/bouts/articles into Postgres via the REST API.
Writes bypass the public read-only RLS, so they use the **secret** key — set it
in a gitignored `.env` at the repo root (never commit it):

```
SUPABASE_URL=https://<ref>.supabase.co
SUPABASE_SECRET_KEY=sb_secret_...      # Supabase -> Project Settings -> API -> secret key
```

With that set, `discover`/`samples` write each new bout through to the DB
automatically; `push` re-syncs everything. Without it, db writes are skipped and
the JSON in `data/` is still written (it remains the source of truth).

`discover` accepts a bout **only if one is actually in the feeds**, and only when
*both* names resolve to real Wikipedia boxer articles with a parseable record
(this filters feed noise such as a stray "Brazil vs Japan" football headline).
`samples` runs **illustrative** matchups (clearly not announced fights), used to
evaluate article variety across divisions and story types.

## Article generation & house style

If `ANTHROPIC_API_KEY` is set, `discover`/`samples` generate the article(s).
Without a key they save the prompt; supply `../data/articles/<slug>.json`
(`{title, slug, body, summary, tags}`) by hand. The current samples were written
this way to demonstrate the finished pages. Wiring the live API later is just
setting the key.

Two prompt levers in `article.py` `SYSTEM_PROMPT`:
- **Variety** — find each fight's specific angle; vary opening, structure, headings.
- **No AI tells** — bans em/en dashes, "not just X but Y", semicolon antithesis,
  rule-of-three, rhetorical "the question is whether…" closers, and filler words.
  `style_check.py` / `python pipeline.py lint` enforces it (em/en dashes are a
  hard failure).

## Enrichment notes

Parsing fixed in `snapshots.py`: age (case + `df=y` ordering), reach/height (bare
inches, fractions, decimals), weight class (`{{plainlist}}` -> array + heaviest
shared division), clean display names (drops `(boxer)`). Still gaps for the
human-in-the-loop: `losses` null when the infobox omits a zero, `nationality`
often blank (-> Wikidata), `standing.rankings` always null (-> sanctioning-body
pages), last-5 form auto-derived and `_unverified`.
