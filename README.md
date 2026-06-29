# BritBoxing

Automated UK boxing news / fight-preview site. See `BOXING_SITE_PLAN.md` for the
full architecture and `CLAUDE.md` for decisions layered on top.

## Repository layout

```
britboxing2.0/
├── data/                 # the "fake DB" — JSON files, source of truth for now
│   ├── articles/<slug>.json    generated preview article {title, slug, body, summary, tags}
│   ├── packages/<slug>.json    bout (+ fighter ids, dates) + both frozen snapshots + prompt
│   ├── prompts/<slug>.txt       human-readable prompt
│   └── fighters/<id>.json       canonical fighter: latest snapshot + bout backlinks
├── pipeline/             # Python content pipeline that GENERATES data/
│   ├── feeds.py          poll boxing RSS into raw items
│   ├── detect.py         AI: is this item a fight announcement? (regex fallback)
│   ├── snapshots.py      Wikipedia -> JSONB snapshot; sparse record when no article
│   ├── fighters.py       fighters DB: stable ids, upsert, bout backlinks (dedup anchor)
│   ├── article.py        build the Claude prompt; call the API when a key is set
│   ├── pipeline.py       orchestrator: discover / samples / lint
│   ├── style_check.py    flags "AI tell" patterns in copy
│   └── sample_matchups.json
├── web/                  # Nuxt 3 site; reads data/ via its own server API
│   ├── server/api/       Nitro routes = "the API": /fights + /fighters (read data/, C# later)
│   ├── pages/            index, fights/[slug], fighters/ + fighters/[id]
│   └── components/FightCard.vue
├── britboxing_wikifetch.py   # original Wikipedia fetcher prototype (root)
├── BOXING_SITE_PLAN.md  CLAUDE.md
```

## How the pieces fit

1. **`pipeline/`** turns a bout into JSON and writes it to **`data/`**.
2. **`web/`** (Nuxt) serves `/api/fights` from its Nitro server, which reads
   **`data/`**. The Vue pages only ever call `/api/fights` — they don't know the
   data is local JSON, so when the real **C# API** exists it drops in by pointing
   those routes (or the fetch base URL) at it.
3. Later, `cd web && npm run generate` prerenders the whole site to static HTML
   for Cloudflare Pages. The filesystem reads happen at build time.

## Run it

```bash
# 1. generate / refresh the data (Python)
cd pipeline
pip install requests mwparserfromhell feedparser anthropic
python pipeline.py discover     # find a real bout from the feeds
python pipeline.py samples      # build the illustrative sample previews
python pipeline.py lint         # check copy for AI tells

# 2. run the site (Node)
cd ../web
npm install
npm run dev                     # http://localhost:3000
```

Article generation calls the Claude API when `ANTHROPIC_API_KEY` is set; without
it the pipeline saves the prompt and the article JSON is supplied by hand (as the
current samples were). See `pipeline/README.md` for detail.
