# CLAUDE.md — BritBoxing project handoff

Orientation for Claude Code sessions working on this project.

## Read first
- **`BOXING_SITE_PLAN.md`** is the architecture source of truth: tech stack, DB
  schema, JSONB snapshot format, content pipeline, build phases. Read it fully
  before scaffolding anything.
- **This file** records decisions and corrections layered on top of the plan
  since it was written. **Where this file conflicts with the plan, this file
  wins** — the specific conflicts are called out under "Corrections" below.

## Project in one line
Automated UK boxing news / fight-preview site. Branding: **BritBoxing**.
Domains owned: `britboxing.co.uk` + `britboxing.com`.
→ Run `.co.uk` as the canonical domain and **301-redirect `.com` → `.co.uk`** so
SEO authority isn't split across two domains. UK-journalism tone (already in the plan).

## Data sourcing policy  ← main thing decided since the plan
The plan named a `FighterEnrichmentService` but never pinned down where verified
fighter data actually comes from. Resolved:

- **Primary record source: English Wikipedia (MediaWiki API) + Wikidata.**
  Licensed for reuse — Wikipedia text is CC BY-SA 4.0 (attribute Wikipedia when
  you display derived text); Wikidata structured data is CC0. A working prototype
  fetcher already exists: **`britboxing_wikifetch.py`** — it pulls a fighter's
  record + bio and emits the exact JSONB snapshot shape. Validate data quality
  with it, then **port the logic into `FighterEnrichmentService.cs`**.
  - Set a real `USER_AGENT` (Wikimedia API policy requires a descriptive UA with
    contact info).
  - The `--records` last-5 derivation is **best-effort / unverified** — Wikipedia
    bout tables vary in layout. Eyeball it; don't freeze it blind.
- **Rankings: the sanctioning bodies (WBC / WBO / IBF / WBA) ratings pages.**
  Public, update ~monthly, so a low-volume monthly read is reasonable. Verify each
  site's `robots.txt` / terms first; prefer a downloadable ratings doc over HTML
  scraping where offered. Wikipedia infoboxes do **not** reliably carry sanctioning
  rankings, which is why `standing.rankings` comes out null from the fetcher.
- **Do NOT build a BoxRec scraper.** BoxRec's terms prohibit automated access and
  they actively block it. Use BoxRec as a *human* reference only (open in a tab
  while verifying). Reinforces plan decision #6.
- **Do NOT scrape Matchroom / Queensberry.** Commercial sites, restrictive terms,
  no API. The only thing you need from them — "a fight has been announced" —
  already arrives via the Sky / BBC / ESPN news RSS feeds in the plan.
- **Human in the loop at card-creation time.** Build a small validating
  "create match" admin form: paste each fighter's record, it checks shape against
  the schema and sanity-checks the numbers (wins + losses + draws + NC == total,
  winsKo <= wins, etc.), then freezes the snapshot. This fits the snapshot model
  and removes any need for a continuous automated feed.

## Corrections to BOXING_SITE_PLAN.md
Read these before trusting the plan verbatim.

1. **Render free tier sleeps after ~15 min idle → Hangfire cron won't fire
   reliably.** The plan's "all free, ~£4/month" assumes an always-on process.
   Either budget the always-on Render instance from the start (~£7/mo), or move
   scheduling to an external trigger that pings the API: Supabase `pg_cron`, a
   Cloudflare Worker cron trigger, or a GitHub Actions scheduled workflow (all
   free and more robust than a sleeping web service).
2. **Twitter/X free tier is write-only**, with a low monthly post cap and no read
   access. Fine for auto-posting a few cards a day; don't design anything that
   *reads* from the API on the free tier.
3. **Scaled auto-published AI content is a real SEO risk** (Google's
   scaled-content-abuse policy targets mass-generated pages). Keep the
   draft/manual-review gate longer than the plan's Phase 4 implies. The
   head-to-head stat cards are the genuinely useful, hard-to-fake part of each
   page — lean on the data, not the prose.
4. **UK gambling-affiliate compliance is mandatory.** ASA/CAP rules require
   age-gating, "18+", and responsible-gambling / GamCare messaging on any page
   carrying betting links. AdSense is restrictive about gambling-adjacent content
   (Ezoic / Mediavine are more lenient). Build this into the page templates from
   the start rather than bolting it on.
   → Drops the plan's FEEDS comment about a "light scrape" of promoter event pages:
   news RSS is the announcement trigger; promoter sites aren't scraped.

## Assets already produced
- **`britboxing_wikifetch.py`** — Wikipedia record/bio fetcher → snapshot JSON.
  Prototype to validate, then port to C#.
- **Seed fighter list** (British, by division — **names only, records UNVERIFIED**;
  confirm each via the fetcher before seeding). Britain is deep heavyweight →
  welterweight and thin below, so a strict "top 10 each" is artificial in the
  lighter classes. Women's divisions included — GB is world-class there.
  - **Heavyweight:** Daniel Dubois, Anthony Joshua, Fabio Wardley, Moses Itauma,
    Joe Joyce, Frazer Clarke, Derek Chisora, Dillian Whyte, David Adeleye,
    Johnny Fisher (Tyson Fury — verify retirement status)
  - **Cruiserweight:** Richard Riakporhe, Chris Billam-Smith, Isaac Chamberlain,
    Mikael Lawal, Cheavon Clarke, Viddal Riley
  - **Light Heavyweight:** Joshua Buatsi, Anthony Yarde, Callum Smith, Dan Azeez,
    Karol Itauma, Ben Whittaker, Lyndon Arthur, Craig Richards, Willy Hutchinson
  - **Super Middleweight:** Hamzah Sheeraz (verify weight), Zach Parker,
    Lerrone Richards, Mark Heffron
  - **Middleweight:** Chris Eubank Jr, Felix Cash, Denzel Bentley, Nathan Heaney,
    Brad Pauls
  - **Super Welterweight:** Liam Smith, Josh Kelly, Sam Eggington
  - **Welterweight:** Conor Benn, Ekow Essuman, Chris Kongo, Harry Scarff
  - **Super Lightweight:** Jack Catterall, Dalton Smith, Adam Azim, Sam Maxwell
    (Josh Taylor — verify retirement)
  - **Lightweight:** Sam Noakes, Mark Chamberlain, Gavin Gwynne, Maxi Hughes
  - **Super Featherweight:** Anthony Cacace, Archie Sharp (Joe Cordina — verify weight)
  - **Featherweight:** Nick Ball, Leigh Wood (Josh Warrington — verify status)
  - **Flyweight & lower:** Sunny Edwards, Galal Yafai (very thin below this)
  - **Women's:** Savannah Marshall, Natasha Jonas, Lauren Price, Caroline Dubois,
    Ellie Scotney, Chantelle Cameron, Sandy Ryan, Rhiannon Dixon, Terri Harper

## Suggested first task
Phase 1 from the plan: scaffold the C# Web API + EF Core, create the Supabase
Postgres schema (fighters / events / bouts / articles), then scaffold the Nuxt
site shell with the core components. Seed 10–20 fighters from the list above using
**verified** records (run `britboxing_wikifetch.py`, eyeball the output, then
insert). Hold off on Twitter and full automation until the data + review flow is solid.

## Open questions to confirm with the developer
- Canonical domain confirmed as `.co.uk`? (assumed yes)
- Scheduler choice: always-on Render instance, or external cron trigger?
- Initial fighter pool size: marquee names only, or full top-10-per-division?
