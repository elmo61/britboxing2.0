<script setup lang="ts">
const route = useRoute()
const id = route.params.id as string
const { data, error } = await useFetch(`/api/fighters/${id}`)

if (error.value || !data.value) {
  throw createError({ statusCode: 404, statusMessage: 'Fighter not found', fatal: true })
}

const f = data.value.fighter
const bouts = data.value.bouts
const rec = f.latest?.record ?? {}
const phys = f.latest?.physical ?? {}

function recordLine(r: any): string {
  if (r.wins == null) return '—'
  return `${r.wins}-${r.losses ?? 0}-${r.draws ?? 0}`
}
function koPct(r: any): number | null {
  if (!r.wins || r.winsKo == null) return null
  return Math.round((r.winsKo / r.wins) * 100)
}
function height(i: number | null): string {
  return i ? `${Math.floor(i / 12)}′${i % 12}″` : '—'
}

useHead({ title: `${f.name} | BritBoxing` })
</script>

<template>
  <div class="wrap">
    <NuxtLink to="/fighters" class="back">← All fighters</NuxtLink>
    <div class="kicker">Fighter</div>
    <h1 class="bigname">{{ f.name }}</h1>

    <div v-if="!f.hasWikipedia" class="announce">
      Limited data available for this fighter.
    </div>

    <dl class="profile">
      <div><dt>Record</dt><dd>{{ recordLine(rec) }}</dd></div>
      <div v-if="rec.winsKo != null"><dt>KO wins</dt><dd>{{ rec.winsKo }}<span v-if="koPct(rec) != null"> ({{ koPct(rec) }}%)</span></dd></div>
      <div v-if="phys.stance"><dt>Stance</dt><dd>{{ phys.stance }}</dd></div>
      <div v-if="phys.age != null"><dt>Age</dt><dd>{{ phys.age }}</dd></div>
      <div v-if="phys.heightInches"><dt>Height</dt><dd>{{ height(phys.heightInches) }}</dd></div>
      <div v-if="phys.reachInches"><dt>Reach</dt><dd>{{ phys.reachInches }}″</dd></div>
    </dl>

    <h2>Fights</h2>
    <ul class="bouts">
      <li v-for="b in bouts" :key="b.slug">
        <NuxtLink :to="`/fights/${b.slug}`">vs {{ b.opponentName }}</NuxtLink>
        <span v-if="b.division" class="div">{{ b.division }}</span>
        <span class="status" :class="`status--${b.status}`">{{ b.status }}</span>
      </li>
      <li v-if="!bouts.length" class="muted">No previews yet.</li>
    </ul>

    <footer class="attribution" v-if="f.hasWikipedia && f.latest?._meta?.source">
      <p>Record and biographical data derived from
        <a :href="f.latest._meta.source">English Wikipedia</a>, licensed
        <a href="https://creativecommons.org/licenses/by-sa/4.0/">CC&nbsp;BY-SA&nbsp;4.0</a>.</p>
    </footer>
  </div>
</template>

<style scoped>
.bigname { font-family: var(--font-display); font-weight: 400; text-transform: uppercase; font-size: 2.6rem; line-height: 1; }
.profile { display: grid; gap: 4px; margin: 18px 0 8px; max-width: 360px; }
.profile > div { display: flex; justify-content: space-between; gap: 10px;
  border-bottom: 1px dashed var(--line); padding: 4px 0; }
.profile dt { color: var(--muted); margin: 0; }
.profile dd { margin: 0; font-weight: 600; }
.bouts { list-style: none; padding: 0; margin: 8px 0; }
.bouts li { display: flex; align-items: center; gap: 10px; padding: 8px 0;
  border-bottom: 1px solid var(--line); }
.bouts a { color: var(--gold); text-decoration: none; font-weight: 600; }
.bouts a:hover { text-decoration: underline; }
.div { font-size: .75rem; text-transform: uppercase; letter-spacing: .06em; color: var(--muted); }
.muted { color: var(--muted); }
</style>
