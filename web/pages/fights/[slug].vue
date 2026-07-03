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

function record(s: any): string {
  const r = s?.record ?? {}
  if (r.wins == null) return '—'
  const base = `${r.wins}-${r.losses ?? 0}-${r.draws ?? 0}`
  return r.winsKo != null ? `${base} · ${r.winsKo} KO` : base
}

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

    <div class="poster-band">
      <img class="poster-fighter poster-fighter--l" :src="'/motifs/boxer2.png'" alt="" aria-hidden="true">
      <img class="poster-fighter poster-fighter--r" :src="'/motifs/boxer.png'" alt="" aria-hidden="true">
      <div class="kicker" style="text-align:center">
        Fight preview<template v-if="f.bout.weightClass"> · {{ f.bout.weightClass }}</template>
      </div>
      <div class="poster">
        <div class="corner corner--red">
          <span class="corner__tag">Red corner</span>
          <h1 class="pname">
            <NuxtLink v-if="f.bout.fighterAId" :to="`/fighters/${f.bout.fighterAId}`">{{ f.bout.fighter_a }}</NuxtLink>
            <template v-else>{{ f.bout.fighter_a }}</template>
          </h1>
          <p class="prec">{{ record(f.fighterA) }}</p>
        </div>
        <div class="pvs"><span>VS</span></div>
        <div class="corner corner--blue">
          <span class="corner__tag">Blue corner</span>
          <h1 class="pname">
            <NuxtLink v-if="f.bout.fighterBId" :to="`/fighters/${f.bout.fighterBId}`">{{ f.bout.fighter_b }}</NuxtLink>
            <template v-else>{{ f.bout.fighter_b }}</template>
          </h1>
          <p class="prec">{{ record(f.fighterB) }}</p>
        </div>
      </div>

      <div class="billstrip">
        <span v-if="formatEventDate(f.bout.eventDate)"><strong>{{ formatEventDate(f.bout.eventDate) }}</strong></span>
        <span class="status" :class="`status--${f.bout.status}`">{{ f.bout.status }}</span>
      </div>

      <FightCard :fighter-a="f.fighterA" :fighter-b="f.fighterB" />
    </div>

    <article>
      <h2 class="article-title">{{ f.article.title }}</h2>
      <div class="meta-dates">
        <template v-if="formatPostedAt(f.article.published_at)">Posted {{ formatPostedAt(f.article.published_at) }}</template>
      </div>
      <p class="summary">{{ f.article.summary }}</p>
      <div class="announce">Announced via {{ f.bout.source }}: &ldquo;{{ f.bout.headline }}&rdquo;</div>
      <div v-html="f.article.body" />
    </article>

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

<style scoped>
.article-title {
  font-family: var(--font-cond); font-weight: 600; line-height: 1.1;
  font-size: 1.9rem; margin: 34px 0 6px;
}
</style>
