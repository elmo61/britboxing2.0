// GET /api/fights/:slug  ->  full fight (article + bout + both snapshots).
export default defineEventHandler(async (event) => {
  const slug = getRouterParam(event, 'slug') as string
  try {
    return await getFight(slug)
  } catch {
    throw createError({ statusCode: 404, statusMessage: `Fight '${slug}' not found` })
  }
})
