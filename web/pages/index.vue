<script setup lang="ts">
const { data: articles } = await useFetch('/api/articles')

useHead({
  title: 'BritBoxing · the latest',
  meta: [{ name: 'description', content: 'Data-driven boxing previews and news.' }],
})
useSeoMeta({ ogImage: 'https://britboxing.co.uk/og-default.png', twitterCard: 'summary_large_image', twitterImage: 'https://britboxing.co.uk/og-default.png' }) // static default share card

const route = useRoute()
const router = useRouter()
const division = ref((route.query.division as string) ?? '')
const status = ref((route.query.status as string) ?? '')
watch([division, status], ([d, s]) => {
  router.replace({ query: { ...(d ? { division: d } : {}), ...(s ? { status: s } : {}) } })
})

const DIVISION_ORDER = [
  'Minimumweight', 'Light Flyweight', 'Flyweight', 'Super Flyweight',
  'Bantamweight', 'Super Bantamweight', 'Featherweight', 'Super Featherweight',
  'Lightweight', 'Super Lightweight', 'Welterweight', 'Super Welterweight',
  'Middleweight', 'Super Middleweight', 'Light Heavyweight', 'Cruiserweight', 'Heavyweight',
]
const divisions = computed(() => {
  const present = new Set((articles.value ?? []).map((a: any) => a.division).filter(Boolean))
  return DIVISION_ORDER.filter((d) => present.has(d))
})

const filtered = computed(() => (articles.value ?? []).filter((a: any) =>
  (!division.value || a.division === division.value) &&
  (!status.value || a.status === status.value)))

const featured = computed<any>(() => filtered.value[0] ?? null)
const rest = computed(() => filtered.value.slice(1))

function names(matchup: string): [string, string] {
  const [a, b] = (matchup ?? '').split(' vs ')
  return [a ?? matchup, b ?? '']
}
function cardMeta(a: any): string {
  return [a.division, formatEventDate(a.eventDate)].filter(Boolean).join(' · ')
}
const CARD_MOTIFS = ['/motifs/speedbag.png', '/motifs/boots.png', '/motifs/wraps.png', '/motifs/cornerseat.png', '/motifs/ringrope.png', '/motifs/ringcorner.png', '/motifs/boot.png']
function motif(i: number): string { return CARD_MOTIFS[i % CARD_MOTIFS.length] }
</script>

<template>
  <div class="wrap wrap--wide">
    <div class="controls">
      <h1>Latest</h1>
      <div class="filters">
        <select v-model="division" aria-label="Filter by weight class">
          <option value="">All weight classes</option>
          <option v-for="d in divisions" :key="d" :value="d">{{ d }}</option>
        </select>
        <select v-model="status" aria-label="Filter by status">
          <option value="">Any status</option>
          <option value="confirmed">Confirmed</option>
          <option value="rumoured">Rumoured</option>
          <option value="cancelled">Cancelled</option>
        </select>
      </div>
    </div>

    <div class="grid">
      <NuxtLink v-if="featured" :to="featured.href" class="card card--feat">
        <img class="card__motif" :src="'/motifs/boxer.png'" alt="" aria-hidden="true">
        <div class="card__top">
          <span class="card__flag">◆ Latest</span>
          <span class="status" :class="`status--${featured.status}`">{{ featured.status }}</span>
        </div>
        <div class="card__matchup-lead">{{ names(featured.matchup)[0] }} <span class="card__vs">v</span> {{ names(featured.matchup)[1] }}</div>
        <h2 class="card__headline">{{ featured.title }}</h2>
        <p class="card__sum">{{ featured.summary }}</p>
        <div class="card__foot">
          <span class="card__cta">Read →</span>
          <span v-if="formatPostedAt(featured.postedAt)" class="card__posted">Posted {{ formatPostedAt(featured.postedAt) }}</span>
        </div>
      </NuxtLink>

      <NuxtLink v-for="(a, i) in rest" :key="a.href" :to="a.href" class="card">
        <img class="card__motif" :src="motif(i)" alt="" aria-hidden="true">
        <div class="card__top">
          <span class="status" :class="`status--${a.status}`">{{ a.status }}</span>
          <span class="card__meta">{{ cardMeta(a) }}</span>
        </div>
        <div class="card__matchup">{{ names(a.matchup)[0] }} v {{ names(a.matchup)[1] }}</div>
        <h2 class="card__headline">{{ a.title }}</h2>
        <p class="card__sum">{{ a.summary }}</p>
        <div class="card__foot">
          <span class="card__cta">Read →</span>
          <span v-if="formatPostedAt(a.postedAt)" class="card__posted">Posted {{ formatPostedAt(a.postedAt) }}</span>
        </div>
      </NuxtLink>

      <p v-if="!filtered.length" class="empty">No articles match those filters.</p>
    </div>
  </div>
</template>

<style scoped>
.card__matchup, .card__matchup-lead {
  font-family: var(--font-display); text-transform: uppercase; line-height: 1;
  color: var(--ink);
}
.card__matchup { font-size: 1.4rem; margin: 14px 0 0; }
.card__matchup-lead { font-size: clamp(1.9rem, 4vw, 2.7rem); margin: 18px 0 0; }
.card__vs { color: var(--muted); font-family: var(--font-cond); font-weight: 600; font-size: .7em; }
.card__headline {
  font-family: var(--font-cond); font-weight: 600; font-size: 1.05rem; line-height: 1.25;
  margin: 8px 0 0; color: var(--gold);
}
.card--feat .card__headline { font-size: 1.3rem; }
</style>
