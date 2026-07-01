# BritBoxingFeeds (C#)

The C# backend that pulls upcoming-fight announcements from British boxing news
feeds, extracts structured fields (fighters / date / venue / weight class), and
dedupes the same fight reported by multiple sources. Adapted from an example
solution; being migrated to full parity with (and eventually replacing) the
Python `pipeline/`.

## Structure

```
src/
  BritBoxingFeeds.Core/          FightAnnouncement model, IFightSource, FightAggregator
  BritBoxingFeeds.Sources/       one class per feed — RssFightSourceBase + BBC / BoxingScene /
                                 WorldBoxingNews / YouTube (+ MatchroomSource, not registered:
                                 promoter scraping is out of scope per ../CLAUDE.md)
  BritBoxingFeeds.Extraction/    Regex (free) + Anthropic (LLM) + Composite (regex-first) extractors
  BritBoxingFeeds.Deduplication/ merge the same fight across sources (name-pair + date tolerance)
  BritBoxingFeeds.App/           console runner (DI wiring)
```

## Run

Requires the .NET 8 SDK.

```bash
cd backend
dotnet run --project src/BritBoxingFeeds.App
```

- **`NuGet.config`** pins restore to nuget.org (`<clear/>` drops any inherited
  private/enterprise feeds that would 401 on a personal build).
- With **`ANTHROPIC_API_KEY`** set, extraction uses the regex-first + LLM
  composite (fills fighters/date/venue/title from messy headlines + article
  bodies). Without it, the app runs **regex-only** — clean "X vs Y" headlines
  resolve, everything else is left blank for a human/LLM pass.

## Status / next

- Builds clean on net8; runs and collects live feeds (confirmed it captures new
  announcements, e.g. the Fury–Wach fight from BBC).
- **Not yet ported from Python:** Wikipedia enrichment → fighter snapshots,
  article generation (house style), and Supabase writes. Those land here next,
  then the Python `pipeline/` is retired.
- BoxingScene's `/rss` currently returns nothing (dead feed) — fix the URL or drop it.
