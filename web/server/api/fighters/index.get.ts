// GET /api/fighters  ->  roster list.
export default defineEventHandler(async () => {
  const ids = await listFighterIds()
  const fighters = await Promise.all(ids.map(async (id) => {
    const { fighter, bouts } = await getFighterProfile(id)
    const rec = fighter.latest?.record ?? {}
    return {
      id,
      name: fighter.name,
      record: rec.wins == null ? null : `${rec.wins}-${rec.losses ?? 0}-${rec.draws ?? 0}`,
      hasWikipedia: !!fighter.hasWikipedia,
      boutCount: bouts.length,
    }
  }))
  return fighters
})
