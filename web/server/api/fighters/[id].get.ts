// GET /api/fighters/:id  ->  fighter profile + their bouts (from Supabase).
export default defineEventHandler((event) =>
  getFighterProfile(getRouterParam(event, 'id') as string),
)
