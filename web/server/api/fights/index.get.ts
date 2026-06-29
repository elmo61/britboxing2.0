// GET /api/fights  ->  summary list for the home page.
export default defineEventHandler(async () => {
  const slugs = await listSlugs()
  const fights = await Promise.all(slugs.map(async (slug) => {
    const { article, bout } = await getFight(slug)
    return {
      slug,
      title: article.title ?? slug,
      summary: article.summary ?? '',
      matchup: `${bout.fighter_a} vs ${bout.fighter_b}`,
      division: bout.weightClass ?? '',
    }
  }))
  return fights
})
