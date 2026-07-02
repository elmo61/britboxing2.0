<script setup lang="ts">
const route = useRoute()
const slug = route.params.slug as string

const { data: fight, error } = await useFetch(`/api/fights/${slug}`)

if (error.value || !fight.value) {
  throw createError({ statusCode: 404, statusMessage: 'Fight not found', fatal: true })
}

const f = fight.value
const wikiSources = [
  { label: `${f.bout.fighter_a} (Wikipedia)`, url: f.fighterA._meta.source },
  { label: `${f.bout.fighter_b} (Wikipedia)`, url: f.fighterB._meta.source },
]

useHead(() => ({
  title: `${f.article.title} | BritBoxing`,
  meta: [
    { name: 'description', content: f.article.summary },
    { property: 'og:title', content: f.article.title },
    { property: 'og:description', content: f.article.summary },
    { name: 'twitter:card', content: 'summary_large_image' },
  ],
}))
</script>

<template>
  <div class="wrap" v-if="f">
    <NuxtLink to="/" class="back">← All previews</NuxtLink>

    <div class="masthead"><BritLogo /></div>
    <div class="kicker">
      Fight preview<template v-if="f.bout.weightClass"> · {{ f.bout.weightClass }}</template>
      <span class="status" :class="`status--${f.bout.status}`">{{ f.bout.status }}</span>
    </div>
    <h1>{{ f.article.title }}</h1>
    <div class="meta-dates">
      <template v-if="formatEventDate(f.bout.eventDate)">Fight date <strong>{{ formatEventDate(f.bout.eventDate) }}</strong></template>
      <template v-if="formatEventDate(f.bout.eventDate) && formatPostedAt(f.article.published_at)"> · </template>
      <template v-if="formatPostedAt(f.article.published_at)">Posted {{ formatPostedAt(f.article.published_at) }}</template>
    </div>
    <p class="summary">{{ f.article.summary }}</p>
    <div class="announce">
      Announced via {{ f.bout.source }}: &ldquo;{{ f.bout.headline }}&rdquo;
    </div>

    <FightCard
      :fighter-a="f.fighterA"
      :fighter-b="f.fighterB"
      :href-a="f.bout.fighterAId ? `/fighters/${f.bout.fighterAId}` : undefined"
      :href-b="f.bout.fighterBId ? `/fighters/${f.bout.fighterBId}` : undefined"
    />

    <article v-html="f.article.body" />

    <div class="tags">
      <span v-for="(t, i) in f.article.tags" :key="i">{{ t }}</span>
    </div>

    <footer class="attribution">
      <p>
        <strong>Sources &amp; attribution.</strong>
        Fighter records and biographical data derived from English Wikipedia
        (<template v-for="(s, i) in wikiSources" :key="i"><a :href="s.url">{{ s.label }}</a><template v-if="i === 0"> · </template></template>),
        licensed
        <a href="https://creativecommons.org/licenses/by-sa/4.0/">CC&nbsp;BY-SA&nbsp;4.0</a>.
      </p>
      <p>Records reflect each fighter's data at the time this preview was published.</p>
    </footer>
  </div>
</template>
