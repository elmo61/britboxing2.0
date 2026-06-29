import { fileURLToPath } from 'node:url'

// https://nuxt.com/docs/api/configuration/nuxt-config
export default defineNuxtConfig({
  compatibilityDate: '2024-11-01',
  devtools: { enabled: false },
  css: ['~/assets/css/main.css'],

  // Server-only config. `dataDir` points at the JSON "database" in ../data.
  // Resolved here (at config load) to an absolute path so the Nitro API routes
  // can read it in dev and during `nuxt generate`. When the real C# API exists,
  // the /api/fights routes get pointed at it instead.
  runtimeConfig: {
    dataDir: fileURLToPath(new URL('../data', import.meta.url)),
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
