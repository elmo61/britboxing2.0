// GET /api/fights/:slug  ->  full fight (from Supabase).
export default defineEventHandler((event) =>
  getFight(getRouterParam(event, 'slug') as string),
)
