<script setup lang="ts">
// A single article. Fight context comes from the bout (poster + tale-of-the-
// tape); the coverage list at the bottom links the other articles on this bout.
const route = useRoute()
const bout = route.params.bout as string
const articleSlug = route.params.article as string

const { data: f, error } = await useFetch(`/api/fights/${bout}`)
if (error.value || !f.value) {
  throw createError({ statusCode: 404, statusMessage: 'Not found', fatal: true })
}

const article = f.value.articles.find((a: any) => a.slug === articleSlug)
if (!article) {
  throw createError({ statusCode: 404, statusMessage: 'Article not found', fatal: true })
}
const others = f.value.articles.filter((a: any) => a.slug !== articleSlug)

const wikiSources = [
  { label: `${f.value.bout.fighter_a} (Wikipedia)`, url: f.value.fighterA?._meta?.source },
  { label: `${f.value.bout.fighter_b} (Wikipedia)`, url: f.value.fighterB?._meta?.source },
]

useHead(() => ({
  title: `${article.title} | BritBoxing`,
  meta: [
    { name: 'description', content: article.summary ?? '' },
    { property: 'og:title', content: article.title },
    { property: 'og:description', content: article.summary ?? '' },
    { name: 'twitter:card', content: 'summary_large_image' },
  ],
}))

// Build-time 1200x630 share card (og:image + twitter:image).
defineOgImage('FightCard', fightCardProps(f.value))
</script>

<template>
  <div class="wrap" v-if="f">
    <NuxtLink to="/" class="back">← Latest</NuxtLink>

    <FightHeader :bout="f.bout" :fighter-a="f.fighterA" :fighter-b="f.fighterB" />
    <ShareBar :title="article.title" :bout-slug="f.slug" />

    <article>
      <h2 class="article-title">{{ article.title }}</h2>
      <div class="meta-dates">
        <span class="status" :class="`status--${article.status}`">{{ article.status }}</span>
        <template v-if="formatPostedAt(article.published_at)"> · Posted {{ formatPostedAt(article.published_at) }}</template>
      </div>
      <p class="summary">{{ article.summary }}</p>
      <div v-html="article.body" />
      <div v-if="article.tags?.length" class="tags">
        <span v-for="(t, i) in article.tags" :key="i">{{ t }}</span>
      </div>
      <p v-if="article.sources?.length" class="art-sources">
        Reported by
        <template v-for="(s, i) in article.sources" :key="i"><a v-if="s.url" :href="s.url">{{ s.source }}</a><template v-else>{{ s.source }}</template><template v-if="i < article.sources.length - 1"> · </template></template>
      </p>
    </article>

    <ArticleList :articles="others" :bout-slug="f.slug" :current-slug="articleSlug" heading="More on this fight" />
    <p class="hub-link"><NuxtLink :to="`/fights/${f.slug}`">See the full fight page →</NuxtLink></p>

    <footer class="attribution">
      <p>
        <strong>Sources &amp; attribution.</strong>
        Fighter records derived from English Wikipedia
        (<template v-for="(s, i) in wikiSources" :key="i"><a v-if="s.url" :href="s.url">{{ s.label }}</a><template v-if="i === 0 && s.url"> · </template></template>),
        licensed <a href="https://creativecommons.org/licenses/by-sa/4.0/">CC&nbsp;BY-SA&nbsp;4.0</a>.
      </p>
    </footer>
  </div>
</template>

<style scoped>
.article-title {
  font-family: var(--font-cond); font-weight: 600; line-height: 1.1;
  font-size: 1.9rem; margin: 34px 0 6px;
}
.hub-link {
  font-family: var(--font-cond); font-weight: 600; letter-spacing: .08em;
  text-transform: uppercase; font-size: .8rem; margin-top: 22px;
}
.hub-link a { color: var(--gold); text-decoration: none; }
.art-sources {
  font-family: var(--font-cond); font-weight: 500; font-size: .8rem;
  letter-spacing: .04em; color: var(--muted); margin-top: 18px;
}
.art-sources a { color: var(--gold); }
</style>
