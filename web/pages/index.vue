<script setup lang="ts">
const { data: fights } = await useFetch('/api/fights')

useHead({
  title: 'BritBoxing · fight previews',
  meta: [{ name: 'description', content: 'Data-driven boxing fight previews.' }],
})

const route = useRoute()
const router = useRouter()

// Filters live in the URL query (?division=&status=) so a filtered view is shareable.
const division = ref((route.query.division as string) ?? '')
const status = ref((route.query.status as string) ?? '')

watch([division, status], ([d, s]) => {
  router.replace({ query: { ...(d ? { division: d } : {}), ...(s ? { status: s } : {}) } })
})

// Weight classes present in the data, lightest to heaviest.
const DIVISION_ORDER = [
  'Minimumweight', 'Light Flyweight', 'Flyweight', 'Super Flyweight',
  'Bantamweight', 'Super Bantamweight', 'Featherweight', 'Super Featherweight',
  'Lightweight', 'Super Lightweight', 'Welterweight', 'Super Welterweight',
  'Middleweight', 'Super Middleweight', 'Light Heavyweight', 'Cruiserweight', 'Heavyweight',
]
const divisions = computed(() => {
  const present = new Set((fights.value ?? []).map((f: any) => f.division).filter(Boolean))
  return DIVISION_ORDER.filter((d) => present.has(d))
})

const filtered = computed(() => (fights.value ?? []).filter((f: any) =>
  (!division.value || f.division === division.value) &&
  (!status.value || f.status === status.value)))

// Tiers: featured = newest confirmed (else newest), then standard, then compact.
const featured = computed<any>(() => filtered.value.find((f: any) => f.status === 'confirmed') ?? filtered.value[0] ?? null)
const others = computed(() => filtered.value.filter((f: any) => f !== featured.value))
const standard = computed(() => others.value.slice(0, 6))
const mini = computed(() => others.value.slice(6))

function names(matchup: string): [string, string] {
  const [a, b] = (matchup ?? '').split(' vs ')
  return [a ?? matchup, b ?? '']
}
function cardMeta(f: any): string {
  const d = formatEventDate(f.eventDate)
  return [f.division, d].filter(Boolean).join(' · ')
}

// Faint equipment motifs behind the cards, rotating for variety.
const CARD_MOTIFS = ['/motifs/speedbag.png', '/motifs/boots.png', '/motifs/wraps.png', '/motifs/cornerseat.png', '/motifs/ringrope.png', '/motifs/ringcorner.png', '/motifs/boot.png']
function motif(i: number): string { return CARD_MOTIFS[i % CARD_MOTIFS.length] }
</script>

<template>
  <div class="wrap wrap--wide">
    <div class="controls">
      <h1>Fight previews</h1>
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
      <!-- featured -->
      <NuxtLink v-if="featured" :to="`/fights/${featured.slug}`" class="card card--feat">
        <img class="card__motif" :src="'/motifs/boxer.png'" alt="" aria-hidden="true">
        <div class="card__top">
          <span v-if="featured.status === 'confirmed'" class="card__flag">◆ Latest confirmed</span>
          <span v-else class="card__flag">◆ Latest</span>
          <span class="status" :class="`status--${featured.status}`">{{ featured.status }}</span>
        </div>
        <div class="card__fighters">{{ names(featured.matchup)[0] }} <span class="card__vs">v</span> {{ names(featured.matchup)[1] }}</div>
        <div class="card__meta" style="margin-top:8px">{{ cardMeta(featured) }}</div>
        <p class="card__sum">{{ featured.summary }}</p>
        <span class="card__cta">Read the full preview →</span>
      </NuxtLink>

      <!-- standard -->
      <NuxtLink v-for="(f, i) in standard" :key="f.slug" :to="`/fights/${f.slug}`" class="card">
        <img class="card__motif" :src="motif(i)" alt="" aria-hidden="true">
        <div class="card__top">
          <span class="status" :class="`status--${f.status}`">{{ f.status }}</span>
          <span class="card__meta">{{ cardMeta(f) }}</span>
        </div>
        <div class="card__fighters">{{ names(f.matchup)[0] }} <span class="card__vs">v</span> {{ names(f.matchup)[1] }}</div>
        <p class="card__sum">{{ f.summary }}</p>
        <span class="card__cta">Read preview →</span>
      </NuxtLink>

      <!-- compact tail -->
      <template v-if="mini.length">
        <div class="more-head">Earlier previews</div>
        <NuxtLink v-for="(f, i) in mini" :key="f.slug" :to="`/fights/${f.slug}`" class="card card--mini">
          <img class="card__motif" :src="motif(i + 3)" alt="" aria-hidden="true">
          <div class="card__top">
            <span class="status" :class="`status--${f.status}`">{{ f.status }}</span>
            <span class="card__meta">{{ cardMeta(f) }}</span>
          </div>
          <div class="card__fighters">{{ names(f.matchup)[0] }} <span class="card__vs">v</span> {{ names(f.matchup)[1] }}</div>
        </NuxtLink>
      </template>

      <p v-if="!filtered.length" class="empty">No previews match those filters.</p>
    </div>
  </div>
</template>
