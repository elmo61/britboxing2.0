# Boxing News & Fight Preview Site — Project Plan

## Overview

An automated boxing news and fight preview website. When a fight is announced, the pipeline automatically fetches fighter stats, generates a written preview article via Claude AI, produces shareable social media images, publishes a static page, and posts to Twitter/X. The goal is to build organic SEO traffic and monetise via betting affiliate links and display advertising.

---

## Goals

- Auto-generate fight preview articles the moment a bout is announced
- Produce shareable 1200×630 fight card images for Twitter/X and Open Graph
- Build SEO traffic through structured, data-driven content
- Monetise via betting affiliates (bet365, Sky Bet etc) and display ads (Ezoic → Mediavine)
- Run at near-zero hosting cost using static site generation

---

## Tech Stack

| Layer | Technology | Reason |
|---|---|---|
| Frontend | Nuxt.js (Vue) — static generated | Free hosting, fast, SEO-friendly |
| Static Hosting | Cloudflare Pages | Free, unlimited bandwidth, CDN built in |
| Backend API | C# ASP.NET Core Web API | Developer preference |
| Background Jobs | Hangfire | Cron scheduling, dashboard, retry logic |
| Database | PostgreSQL via Supabase | Free tier, JSONB support |
| ORM | EF Core | C# standard |
| AI | Claude API (claude-sonnet-4-6) | Article generation |
| Image Generation | Node.js microservice (Puppeteer) | HTML → PNG screenshot |
| Image Storage | Cloudflare R2 | Free 10GB, no egress fees |
| Social Posting | Twitter/X API | Auto-post fight cards |

---

## Architecture

```
Fight announced (RSS/promoter feed)
        ↓
C# Hangfire job triggers
        ↓
Fetch both fighters from DB
Capture JSONB snapshot of stats at announcement time
        ↓
Call Claude API → generate preview article
        ↓
Call Node image service → generate 1200×630 fight card PNG
Upload PNG to Cloudflare R2
        ↓
Save article + image URL to PostgreSQL
        ↓
Trigger Cloudflare Pages deploy webhook → nuxt generate
        ↓
New static fight page goes live
        ↓
Twitter bot auto-posts fight card image + article link
```

---

## Project Structure

### C# API (`/BoxingApi`)

```
BoxingApi/
├── Controllers/
│   ├── FightsController.cs
│   ├── FightersController.cs
│   └── ArticlesController.cs
├── Services/
│   ├── FeedIngestionService.cs        # RSS polling every 6 hours
│   ├── ArticleGenerationService.cs    # Claude API calls
│   ├── FighterEnrichmentService.cs    # fills stat gaps
│   ├── ImageGenerationService.cs      # calls Node image microservice
│   └── TwitterService.cs             # auto-posting
├── Jobs/
│   └── PipelineJob.cs                # Hangfire cron job
├── Models/
│   ├── Fighter.cs
│   ├── Bout.cs
│   ├── Event.cs
│   └── Article.cs
└── Data/
    └── BoxingDbContext.cs
```

### Node Image Service (`/image-service`)

```
image-service/
├── index.js               # Express app, POST /generate-image
├── templates/
│   ├── fight-announcement.html
│   ├── fight-result.html
│   └── fighter-stats.html
└── upload.js              # Cloudflare R2 upload helper
```

### Nuxt Frontend (`/boxing-site`)

```
boxing-site/
├── pages/
│   ├── index.vue                          # home, latest fights
│   ├── fighters/[slug].vue               # fighter profile (live record)
│   ├── fights/[slug].vue                 # fight preview (snapshot data)
│   ├── fights/[slug]/result.vue          # result page
│   └── weight-classes/[slug].vue         # division page
├── components/
│   ├── FightCard.vue                     # head to head stat card
│   ├── FighterForm.vue                   # last 5/10 fights visual
│   ├── StatBar.vue                       # KO rate, reach etc
│   └── BoutList.vue                      # list of upcoming/recent fights
└── public/
    └── search-index.json                 # generated at build, client-side search
```

---

## Database Schema

### fighters

```sql
CREATE TABLE fighters (
    id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    slug VARCHAR(100) UNIQUE NOT NULL,
    nationality VARCHAR(50),
    weight_class VARCHAR(50),
    age INT,
    stance VARCHAR(20),         -- orthodox / southpaw
    reach_inches INT,
    height_inches INT,
    wins INT DEFAULT 0,
    losses INT DEFAULT 0,
    draws INT DEFAULT 0,
    wins_ko INT DEFAULT 0,
    wins_dec INT DEFAULT 0,
    current_streak INT DEFAULT 0,
    streak_type VARCHAR(20),    -- 'KO' | 'Decision' | 'Mixed'
    kos_in_last_five INT DEFAULT 0,
    titles_held TEXT[],
    wbc_ranking INT,
    wbo_ranking INT,
    ibf_ranking INT,
    wba_ranking INT,
    boxrec_id VARCHAR(50),      -- reference only, not scraped
    last_updated TIMESTAMPTZ DEFAULT NOW()
);
```

### events

```sql
CREATE TABLE events (
    id SERIAL PRIMARY KEY,
    title VARCHAR(200),
    slug VARCHAR(200) UNIQUE,
    event_date DATE,
    venue VARCHAR(200),
    promoter VARCHAR(100),
    card_type VARCHAR(50)       -- 'world_title' | 'domestic' | 'show'
);
```

### bouts

```sql
CREATE TABLE bouts (
    id SERIAL PRIMARY KEY,
    event_id INT REFERENCES events(id),
    fighter_a_id INT REFERENCES fighters(id),
    fighter_b_id INT REFERENCES fighters(id),
    weight_class VARCHAR(50),
    title_at_stake BOOLEAN DEFAULT FALSE,
    belt_description VARCHAR(200),
    scheduled_rounds INT,
    
    -- Snapshots captured at announcement time (JSONB for flexibility)
    fighter_a_snapshot JSONB,
    fighter_b_snapshot JSONB,
    
    -- Result (filled after fight)
    result_winner_id INT REFERENCES fighters(id),
    result_method VARCHAR(50),  -- 'KO' | 'TKO' | 'UD' | 'SD' | 'MD' | 'DQ'
    result_round INT,
    
    announced_at TIMESTAMPTZ DEFAULT NOW()
);
```

### articles

```sql
CREATE TABLE articles (
    id SERIAL PRIMARY KEY,
    title VARCHAR(300),
    slug VARCHAR(300) UNIQUE,
    body TEXT,
    summary VARCHAR(500),
    category VARCHAR(50),       -- 'preview' | 'result' | 'news' | 'history'
    tags TEXT[],
    bout_id INT REFERENCES bouts(id),
    og_image_url VARCHAR(500),  -- Cloudflare R2 URL
    ai_generated BOOLEAN DEFAULT TRUE,
    published_at TIMESTAMPTZ DEFAULT NOW()
);
```

---

## JSONB Snapshot Format

Fighter stats are snapshotted as JSONB at the moment a fight is announced. This means fight preview pages always show the record **at time of announcement**, not the fighter's current record later in their career.

```json
{
  "capturedAt": "2024-11-15T10:30:00Z",
  "version": 1,

  "record": {
    "wins": 34,
    "losses": 1,
    "draws": 0,
    "winsKo": 24,
    "winsDec": 10
  },

  "form": {
    "currentStreak": 3,
    "streakType": "KO",
    "last5": ["WKO", "WKO", "WKO", "WDEC", "LDEC"],
    "kosInLastFive": 3,
    "avgRoundsLastFive": 4.2
  },

  "physical": {
    "age": 36,
    "heightInches": 82,
    "reachInches": 85,
    "stance": "Orthodox"
  },

  "standing": {
    "titlesHeld": ["WBC Heavyweight"],
    "rankings": {
      "wbc": 1,
      "wbo": 2,
      "ibf": null,
      "wba": 3
    }
  }
}
```

The `version` field allows the Vue components to handle schema changes over time without breaking old pages. Add new fields to new snapshots freely — old snapshots remain valid.

---

## Content Pipeline Detail

### Feed Sources

```javascript
const FEEDS = {
  news: [
    'https://www.skysports.com/rss/12040',         // Sky Sports Boxing
    'https://www.bbc.co.uk/sport/boxing/rss.xml',  // BBC Boxing
    'https://www.espn.com/espn/rss/boxing/news',   // ESPN Boxing
  ],
  // Promoter upcoming event pages (light scrape, changes rarely):
  // Matchroom, Top Rank, PBC
  
  // Rankings pages (monthly scrape):
  // WBC, WBO, IBF, WBA official sites
}
```

### Cron Schedule (Hangfire)

- **Every 6 hours** — poll all RSS feeds, detect new bout announcements
- **On new bout** — immediately trigger preview article + image generation + deploy
- **Morning after fight date** — trigger result article generation
- **Weekly** — refresh fighter rankings, rebuild division pages

### Article Generation Prompt Structure

The Claude API prompt is built dynamically from the JSONB snapshot data:

1. Fight details (date, venue, title at stake)
2. Fighter A full snapshot
3. Fighter B full snapshot
4. Article structure instructions:
   - Opening narrative
   - Fighter A breakdown (style, form, strengths)
   - Fighter B breakdown
   - Key stat comparisons
   - Tactical analysis
   - Verdict with method prediction
5. Tone: UK boxing journalism, factual, no clichés
6. Instruction: do NOT invent stats, only use provided data
7. Return format: JSON `{ title, slug, body, summary, tags }`

---

## Shareable Image Generation

### Image Types

| Image | Trigger | Size |
|---|---|---|
| Fight announcement card | Bout announced | 1200×630 |
| Fighter stats card | Bout announced | 1200×630 |
| Fight result card | Morning after fight | 1200×630 |

### Image Service Flow

```
C# API → POST /generate-image { template, data }
              ↓
Node/Puppeteer renders HTML template at 1200×630
              ↓
Screenshots to PNG
              ↓
Uploads to Cloudflare R2
              ↓
Returns CDN URL to C# API
```

### Open Graph Tags (Nuxt)

Every fight page sets OG tags so any shared URL renders the fight card automatically on Twitter, WhatsApp, Discord, Reddit etc:

```html
<meta property="og:image" content="https://cdn.yoursite.com/fury-vs-usyk-card.png" />
<meta name="twitter:card" content="summary_large_image" />
<meta name="twitter:image" content="https://cdn.yoursite.com/fury-vs-usyk-card.png" />
```

### Design Principles for Image Templates

- Dark background (looks premium, screenshots well)
- Fighter names large and readable at thumbnail size
- Site URL/logo small but present on every image (brands every share)
- Limited palette — red/gold/black works well for boxing
- Show: record, KO rate, current streak only — not everything

---

## Static Site Strategy

### Page Types and Data Sources

| Page | Data source | Rebuilt when |
|---|---|---|
| `/fights/[slug]` | JSONB snapshot (frozen at announcement) | Never changes |
| `/fights/[slug]/result` | Snapshot + updated fighter record | Morning after fight |
| `/fighters/[slug]` | Live fighter record | After every fight |
| `/weight-classes/[slug]` | Live rankings | Weekly |
| `/` | Latest bouts list | Every new bout |

### Deploy Trigger

After each article + image is generated, the C# pipeline hits the Cloudflare Pages deploy webhook:

```csharp
await _http.PostAsync(
    "https://api.cloudflare.com/client/v4/pages/webhooks/deploy/YOUR_HOOK_ID",
    null
);
```

Cloudflare runs `nuxt generate` and deploys the updated static site. Takes 2-5 minutes. Acceptable for a boxing site where fights aren't announced every few minutes.

### Search

No server needed. At build time, generate a `search-index.json` containing all fights and fighters. Vue does client-side filtering. No Algolia needed until scale demands it.

---

## Monetisation Plan

### Phase 1 — From launch

- **Betting affiliates** — bet365, Sky Bet, Paddy Power. Pay £50-100+ per referred signup. Boxing fans bet. Add affiliate links to every fight preview.
- Apply to **AWIN** or direct affiliate programmes.

### Phase 2 — Once traffic starts

- **Google AdSense** — low bar to entry, add once any traffic exists
- **Ezoic** — better rates than AdSense, approves smaller sites

### Phase 3 — Scale target

- **Mediavine** — requires 50k sessions/month, significantly better RPM
- At this point ad revenue likely exceeds affiliate revenue

---

## Running Costs

| Service | Cost |
|---|---|
| Nuxt static site (Cloudflare Pages) | Free |
| Cloudflare R2 image storage | Free (first 10GB) |
| C# API (Render free tier) | Free |
| Hangfire cron (runs within Render) | Free |
| Node image service (Render free tier) | Free |
| PostgreSQL (Supabase free tier) | Free |
| Claude API (~4 articles/day) | ~£4/month |
| Twitter API (basic posting) | Free |
| **Total** | **~£4/month** |

When traffic grows, upgrade Render to always-on instance (~£7/month) to avoid cold start delays on the API.

---

## Build Phases

### Phase 1 — Foundation (Weeks 1-2)
- Scaffold C# Web API with EF Core
- Set up Supabase PostgreSQL, run migrations
- Build fighter and bout CRUD endpoints
- Scaffold Nuxt site with basic page structure and Vue components (FightCard, FighterForm, StatBar)
- Manually seed 10-20 fighters with accurate data

### Phase 2 — AI Pipeline (Weeks 3-4)
- Integrate Claude API in ArticleGenerationService
- Build and test prompt with real fight data
- Add draft/review flag — articles go to draft first for manual approval
- Test end-to-end: fighter data → Claude → article saved to DB → page generated

### Phase 3 — Images & Social (Week 5)
- Build Node image microservice
- Design HTML templates for fight card, result card
- Integrate Cloudflare R2 upload
- Set OG meta tags in Nuxt
- Wire up Twitter auto-posting

### Phase 4 — Automation (Week 6)
- Set up Hangfire cron jobs
- Build RSS feed ingestion
- Wire deploy webhook to Cloudflare Pages
- Remove draft flag once quality is consistent — fully automated

### Phase 5 — Ongoing
- Add betting affiliate links
- Fighter profile pages with career timeline
- Historical fight database
- Rankings tracker pages
- "This week in boxing history" evergreen content type

---

## Key Design Decisions & Rationale

1. **JSONB snapshots** — fighter stats frozen at announcement time so fight preview pages are historically accurate forever, regardless of what happens in a fighter's career later

2. **Static site** — no server runtime cost, instant page loads, great for SEO. Rebuild triggered by pipeline on each new fight

3. **Node image microservice separate from C# API** — Puppeteer is heavy; keeps the C# API clean. Communicates via simple HTTP POST

4. **Hangfire for jobs** — much better than raw cron: dashboard, retry on failure, job history, easy scheduling API

5. **Cloudflare Pages + R2** — unlimited bandwidth on Pages means traffic spikes (viral fight card image) don't result in a bill. R2 has no egress fees unlike S3

6. **Don't scrape BoxRec** — use as manual reference only. BoxRec actively blocks scrapers and is a fragile dependency. Use RSS feeds and promoter sites as primary data sources

7. **Snapshot versioning** — `version` field in JSONB allows schema evolution without breaking existing pages

---

## Notes for Next Claude Session

- Developer is comfortable with C#, Vue/Nuxt, Node.js, PostgreSQL
- Hosting preference: Render for API/jobs, Cloudflare Pages for frontend
- Start scaffolding with: C# Web API project → DB schema → Nuxt pages → Claude integration → image service
- Fighter data accuracy is critical — AI must only use data passed in the prompt, never invent records or stats
- UK-focused boxing audience, UK journalism tone
- Betting affiliate monetisation is priority over ads in early stages
