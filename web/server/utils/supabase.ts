import { createClient, type SupabaseClient } from '@supabase/supabase-js'

let client: SupabaseClient | null = null

// Single server-side client built from runtime config (web/.env).
export function useSupabase(): SupabaseClient {
  if (client) return client
  const cfg = useRuntimeConfig()
  client = createClient(cfg.supabaseUrl as string, cfg.supabaseKey as string, {
    auth: { persistSession: false },
  })
  return client
}
