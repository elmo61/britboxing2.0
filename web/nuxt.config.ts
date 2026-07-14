// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2024-11-01',
  devtools: { enabled: false },
  css: ['~/assets/css/main.css'],

  modules: ['@nuxt/fonts', 'nuxt-og-image'],

  // Absolute URL base for og:image tags (and canonicals, when the SEO pass
  // lands). INTERIM: the Render subdomain — britboxing.co.uk is still parked
  // at IONOS and not yet connected to Render. Switch back to
  // https://britboxing.co.uk when the custom domain goes live.
  site: {
    url: 'https://britboxing2-0.onrender.com',
    name: 'BritBoxing',
  },

  // Share-card fonts: og-image v6 reads satori fonts from the @font-face
  // rules @nuxt/fonts emits globally — hence global: true on each family.
  fonts: {
    families: [
      { name: 'Anton', weights: [400], global: true },
      { name: 'Oswald', weights: [500, 600], global: true },
      { name: 'Inter', weights: [400], global: true },
    ],
  },

  // Server-only config. The Nitro API routes read from Supabase using these
  // (the browser-safe publishable pair); values come from web/.env (gitignored).
  runtimeConfig: {
    supabaseUrl: process.env.SUPABASE_URL,
    supabaseKey: process.env.SUPABASE_KEY,
  },

  // For static generation later: crawl the index and prerender every fight page.
  nitro: {
    prerender: { crawlLinks: true, routes: ['/'] },
  },

  app: {
    head: {
      htmlAttrs: { lang: 'en' },
      meta: [
        { charset: 'utf-8' },
        { name: 'viewport', content: 'width=device-width, initial-scale=1' },
      ],
      link: [
        { rel: 'preconnect', href: 'https://fonts.googleapis.com' },
        { rel: 'preconnect', href: 'https://fonts.gstatic.com', crossorigin: '' },
        {
          rel: 'stylesheet',
          href: 'https://fonts.googleapis.com/css2?family=Anton&family=Oswald:wght@400;500;600;700&family=Inter:wght@400;500;600&display=swap',
        },
      ],
    },
  },
})
