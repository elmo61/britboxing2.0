// Data access — now reads from Supabase (Postgres) instead of local JSON.
// The response shapes are unchanged, so the Vue pages don't care where the
// data comes from; they still only call /api/*.
function one<T>(rel: T | T[] | null | undefined): T | undefined {
  // PostgREST returns a to-one embed as an object, but be defensive.
  return Array.isArray(rel) ? rel[0] : (rel ?? undefined)
}

/** Home list: one card per fight, showing its most recent article. */
export async function getFightList() {
  const sb = useSupabase()
  const { data, error } = await sb
    .from('bouts')
    .select('slug, status, weight_class, event_date, fighter_a_snapshot, fighter_b_snapshot, articles!inner(title, summary, published_at)')
  if (error) throw createError({ statusCode: 502, statusMessage: error.message })
  return (data ?? [])
    .map((b: any) => {
      // A bout can have several articles; the card reflects the newest one.
      const arts = (Array.isArray(b.articles) ? b.articles : [b.articles]).filter(Boolean)
      const latest = arts.sort((x: any, y: any) => (y.published_at ?? '').localeCompare(x.published_at ?? ''))[0]
      return {
        slug: b.slug,
        title: latest?.title ?? b.slug,
        summary: latest?.summary ?? '',
        matchup: `${b.fighter_a_snapshot?._meta?.name} vs ${b.fighter_b_snapshot?._meta?.name}`,
        division: b.weight_class ?? '',
        status: b.status ?? 'confirmed',
        postedAt: latest?.published_at ?? null,
        eventDate: b.event_date ?? null,
        articleCount: arts.length,
      }
    })
    .sort((a: any, b: any) => (b.postedAt ?? '').localeCompare(a.postedAt ?? ''))
}

/** Full fight: bout context + both frozen snapshots + all its articles (newest first). */
export async function getFight(slug: string) {
  const sb = useSupabase()
  const { data, error } = await sb
    .from('bouts')
    .select('slug, status, weight_class, event_date, fighter_a_id, fighter_b_id, '
      + 'fighter_a_snapshot, fighter_b_snapshot, '
      + 'articles!inner(id, title, summary, body, tags, ai_generated, published_at, sources)')
    .eq('slug', slug)
    .single()
  if (error || !data) throw createError({ statusCode: 404, statusMessage: `Fight '${slug}' not found` })
  const articles = (Array.isArray(data.articles) ? data.articles : [data.articles])
    .filter(Boolean)
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
    },
    fighterA: data.fighter_a_snapshot,
    fighterB: data.fighter_b_snapshot,
  }
}

/** Roster list, alphabetical, with divisions for filtering. */
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

/** Fighter profile + their bouts (resolved to opponent + division). */
export async function getFighterProfile(id: string) {
  const sb = useSupabase()
  const { data: f, error } = await sb.from('fighters').select('*').eq('id', id).single()
  if (error || !f) throw createError({ statusCode: 404, statusMessage: `Fighter '${id}' not found` })

  const { data: bouts } = await sb
    .from('bouts')
    .select('slug, status, fighter_a_id, fighter_b_id, weight_class')
    .or(`fighter_a_id.eq.${id},fighter_b_id.eq.${id}`)
    .order('slug')

  const oppId = (b: any) => (b.fighter_a_id === id ? b.fighter_b_id : b.fighter_a_id)
  const ids = [...new Set((bouts ?? []).map(oppId))]
  const names: Record<string, string> = {}
  if (ids.length) {
    const { data: opps } = await sb.from('fighters').select('id, name').in('id', ids)
    for (const o of opps ?? []) names[o.id] = o.name
  }

  return {
    fighter: { id: f.id, name: f.name, hasWikipedia: !!f.has_wikipedia, latest: f.latest_snapshot },
    bouts: (bouts ?? []).map((b: any) => ({
      slug: b.slug,
      opponentId: oppId(b),
      opponentName: names[oppId(b)] ?? oppId(b),
      division: b.weight_class ?? '',
      status: b.status ?? 'confirmed',
    })),
  }
}
