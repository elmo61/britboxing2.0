// Data access — reads from Supabase (Postgres). The site's model:
//   bout  = one fight (frozen stats, status, date)  ← the hub
//   article = a news write-up about a bout (many per bout)
// Pages: home = article feed; /fights/[bout] = bout hub; /fights/[bout]/[article]
// = single article; /schedule = upcoming bouts; /fighters + /fighters/[id].

function matchupOf(b: any): string {
  return `${b?.fighter_a_snapshot?._meta?.name ?? '?'} vs ${b?.fighter_b_snapshot?._meta?.name ?? '?'}`
}

/** Home feed: every article, newest first, carrying its bout's context. */
export async function getArticleFeed() {
  const sb = useSupabase()
  const { data, error } = await sb
    .from('articles')
    .select('id, slug, title, summary, status, published_at, '
      + 'bouts!inner(slug, weight_class, event_date, fighter_a_snapshot, fighter_b_snapshot)')
    .order('published_at', { ascending: false })
  if (error) throw createError({ statusCode: 502, statusMessage: error.message })
  return (data ?? []).map((a: any) => {
    const b = Array.isArray(a.bouts) ? a.bouts[0] : a.bouts
    return {
      articleSlug: a.slug,
      boutSlug: b?.slug,
      href: `/fights/${b?.slug}/${a.slug}`,
      title: a.title,
      summary: a.summary ?? '',
      matchup: matchupOf(b),
      division: b?.weight_class ?? '',
      status: a.status ?? b?.status ?? 'confirmed',
      postedAt: a.published_at ?? null,
      eventDate: b?.event_date ?? null,
    }
  })
}

/** Bout hub + article page both use this: the fight + both snapshots + all its articles (newest first). */
export async function getFight(slug: string) {
  const sb = useSupabase()
  const { data, error } = await sb
    .from('bouts')
    .select('slug, status, result, weight_class, event_date, fighter_a_id, fighter_b_id, '
      + 'fighter_a_snapshot, fighter_b_snapshot, '
      + 'articles(id, slug, title, summary, body, tags, status, ai_generated, published_at, sources)')
    .eq('slug', slug)
    .single()
  if (error || !data) throw createError({ statusCode: 404, statusMessage: `Fight '${slug}' not found` })
  const articles = (data.articles ?? [])
    .slice()
    .sort((x: any, y: any) => (y.published_at ?? '').localeCompare(x.published_at ?? ''))
  return {
    slug: data.slug,
    articles,
    bout: {
      fighter_a: data.fighter_a_snapshot?._meta?.name,
      fighter_b: data.fighter_b_snapshot?._meta?.name,
      fighterAId: data.fighter_a_id,
      fighterBId: data.fighter_b_id,
      weightClass: data.weight_class,
      eventDate: data.event_date,
      status: data.status ?? 'confirmed',
      result: data.result ?? null,
    },
    fighterA: data.fighter_a_snapshot,
    fighterB: data.fighter_b_snapshot,
  }
}

/** Scheduled bouts: dated fights soonest-first, then undated ("TBC") below. */
export async function getSchedule() {
  const sb = useSupabase()
  const { data, error } = await sb
    .from('bouts')
    .select('slug, status, result, weight_class, event_date, fighter_a_snapshot, fighter_b_snapshot')
    .order('event_date', { ascending: true, nullsFirst: false })
  if (error) throw createError({ statusCode: 502, statusMessage: error.message })
  return (data ?? []).map((b: any) => ({
    slug: b.slug,
    href: `/fights/${b.slug}`,
    matchup: matchupOf(b),
    division: b.weight_class ?? '',
    status: b.status ?? 'confirmed',
    eventDate: b.event_date,
    result: b.result ?? null,
  }))
}

/** Completed fights with results, newest first, each linked to its result article if one exists. */
export async function getResults() {
  const sb = useSupabase()
  const { data, error } = await sb
    .from('bouts')
    .select('slug, weight_class, event_date, result, fighter_a_snapshot, fighter_b_snapshot, '
      + 'articles(slug, status, published_at)')
    .eq('status', 'completed')
    .order('event_date', { ascending: false, nullsFirst: false })
  if (error) throw createError({ statusCode: 502, statusMessage: error.message })
  return (data ?? []).map((b: any) => {
    const reportArticle = (b.articles ?? [])
      .filter((a: any) => a.status === 'result')
      .sort((x: any, y: any) => (y.published_at ?? '').localeCompare(x.published_at ?? ''))[0]
    return {
      slug: b.slug,
      href: `/fights/${b.slug}`,
      reportHref: reportArticle ? `/fights/${b.slug}/${reportArticle.slug}` : null,
      matchup: matchupOf(b),
      division: b.weight_class ?? '',
      eventDate: b.event_date,
      result: b.result ?? null,
    }
  })
}

/** Roster for the fighters search page. */
export async function getFighterList() {
  const sb = useSupabase()
  const { data, error } = await sb
    .from('fighters')
    .select('id, name, has_wikipedia, wins, losses, draws, weight_classes, nationality')
    .order('name')
  if (error) throw createError({ statusCode: 502, statusMessage: error.message })
  return (data ?? []).map((f: any) => ({
    id: f.id,
    name: f.name,
    record: f.wins == null ? null : `${f.wins}-${f.losses ?? 0}-${f.draws ?? 0}`,
    hasWikipedia: !!f.has_wikipedia,
    divisions: f.weight_classes ?? [],
    nationality: f.nationality ?? null,
  }))
}

/** Fighter hub: profile + their bouts + every article about them, interlinked. */
export async function getFighterProfile(id: string) {
  const sb = useSupabase()
  const { data: f, error } = await sb.from('fighters').select('*').eq('id', id).single()
  if (error || !f) throw createError({ statusCode: 404, statusMessage: `Fighter '${id}' not found` })

  const { data: bouts } = await sb
    .from('bouts')
    .select('slug, status, event_date, fighter_a_id, fighter_b_id, weight_class')
    .or(`fighter_a_id.eq.${id},fighter_b_id.eq.${id}`)

  const boutList = bouts ?? []
  const oppId = (b: any) => (b.fighter_a_id === id ? b.fighter_b_id : b.fighter_a_id)
  const ids = [...new Set(boutList.map(oppId))]
  const names: Record<string, string> = {}
  if (ids.length) {
    const { data: opps } = await sb.from('fighters').select('id, name').in('id', ids)
    for (const o of opps ?? []) names[o.id] = o.name
  }

  // Articles about this fighter = articles of every bout they're in.
  const slugs = boutList.map((b: any) => b.slug)
  let articles: any[] = []
  if (slugs.length) {
    const { data: arts } = await sb
      .from('articles')
      .select('slug, title, status, published_at, bout_slug')
      .in('bout_slug', slugs)
      .order('published_at', { ascending: false })
    articles = (arts ?? []).map((a: any) => ({
      href: `/fights/${a.bout_slug}/${a.slug}`,
      title: a.title,
      status: a.status ?? 'confirmed',
      postedAt: a.published_at,
    }))
  }

  return {
    fighter: { id: f.id, name: f.name, hasWikipedia: !!f.has_wikipedia, latest: f.latest_snapshot },
    bouts: boutList.map((b: any) => ({
      slug: b.slug,
      href: `/fights/${b.slug}`,
      opponentId: oppId(b),
      opponentName: names[oppId(b)] ?? oppId(b),
      division: b.weight_class ?? '',
      status: b.status ?? 'confirmed',
      eventDate: b.event_date,
    })),
    articles,
  }
}
