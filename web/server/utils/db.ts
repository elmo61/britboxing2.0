// Data access — now reads from Supabase (Postgres) instead of local JSON.
// The response shapes are unchanged, so the Vue pages don't care where the
// data comes from; they still only call /api/*.
function one<T>(rel: T | T[] | null | undefined): T | undefined {
  // PostgREST returns a to-one embed as an object, but be defensive.
  return Array.isArray(rel) ? rel[0] : (rel ?? undefined)
}

/** Home list: every bout that has a published article. */
export async function getFightList() {
  const sb = useSupabase()
  const { data, error } = await sb
    .from('bouts')
    .select('slug, weight_class, fighter_a_snapshot, fighter_b_snapshot, articles!inner(title, summary)')
    .order('slug')
  if (error) throw createError({ statusCode: 502, statusMessage: error.message })
  return (data ?? []).map((b: any) => {
    const art = one<any>(b.articles)
    return {
      slug: b.slug,
      title: art?.title ?? b.slug,
      summary: art?.summary ?? '',
      matchup: `${b.fighter_a_snapshot?._meta?.name} vs ${b.fighter_b_snapshot?._meta?.name}`,
      division: b.weight_class ?? '',
    }
  })
}

/** Full fight: article + bout context + both frozen snapshots. */
export async function getFight(slug: string) {
  const sb = useSupabase()
  const { data, error } = await sb
    .from('bouts')
    .select('slug, weight_class, source, headline, fighter_a_id, fighter_b_id, '
      + 'fighter_a_snapshot, fighter_b_snapshot, articles!inner(title, summary, body, tags, ai_generated, slug)')
    .eq('slug', slug)
    .single()
  if (error || !data) throw createError({ statusCode: 404, statusMessage: `Fight '${slug}' not found` })
  return {
    slug: data.slug,
    article: one<any>(data.articles),
    bout: {
      fighter_a: data.fighter_a_snapshot?._meta?.name,
      fighter_b: data.fighter_b_snapshot?._meta?.name,
      fighterAId: data.fighter_a_id,
      fighterBId: data.fighter_b_id,
      weightClass: data.weight_class,
      source: data.source,
      headline: data.headline,
    },
    fighterA: data.fighter_a_snapshot,
    fighterB: data.fighter_b_snapshot,
  }
}

/** Roster list with a fight count per fighter. */
export async function getFighterList() {
  const sb = useSupabase()
  const [fighters, bouts] = await Promise.all([
    sb.from('fighters').select('id, name, has_wikipedia, wins, losses, draws').order('name'),
    sb.from('bouts').select('fighter_a_id, fighter_b_id'),
  ])
  if (fighters.error) throw createError({ statusCode: 502, statusMessage: fighters.error.message })
  const counts: Record<string, number> = {}
  for (const b of bouts.data ?? []) {
    counts[b.fighter_a_id] = (counts[b.fighter_a_id] ?? 0) + 1
    counts[b.fighter_b_id] = (counts[b.fighter_b_id] ?? 0) + 1
  }
  return (fighters.data ?? []).map((f: any) => ({
    id: f.id,
    name: f.name,
    record: f.wins == null ? null : `${f.wins}-${f.losses ?? 0}-${f.draws ?? 0}`,
    hasWikipedia: !!f.has_wikipedia,
    boutCount: counts[f.id] ?? 0,
  }))
}

/** Fighter profile + their bouts (resolved to opponent + division). */
export async function getFighterProfile(id: string) {
  const sb = useSupabase()
  const { data: f, error } = await sb.from('fighters').select('*').eq('id', id).single()
  if (error || !f) throw createError({ statusCode: 404, statusMessage: `Fighter '${id}' not found` })

  const { data: bouts } = await sb
    .from('bouts')
    .select('slug, fighter_a_id, fighter_b_id, weight_class')
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
    })),
  }
}
