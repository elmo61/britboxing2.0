// GET /api/fighters/:id  ->  fighter profile + their bouts.
export default defineEventHandler(async (event) => {
  const id = getRouterParam(event, 'id') as string
  try {
    return await getFighterProfile(id)
  } catch {
    throw createError({ statusCode: 404, statusMessage: `Fighter '${id}' not found` })
  }
})
