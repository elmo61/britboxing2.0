<script setup lang="ts">
// The fight "poster" — shown on both the bout hub and each article page.
// Pulls its data from the bout + both frozen snapshots.
const props = defineProps<{
  bout: Record<string, any>
  fighterA: Record<string, any>
  fighterB: Record<string, any>
}>()

function record(s: any): string {
  const r = s?.record ?? {}
  if (r.wins == null) return '—'
  const base = `${r.wins}-${r.losses ?? 0}-${r.draws ?? 0}`
  return r.winsKo != null ? `${base} · ${r.winsKo} KO` : base
}

// "Winner MethodName R5" strip once the fight is completed.
const resultLine = computed(() => {
  const res = props.bout?.result
  if (!res?.winner) return null
  const method = res.method ? ` by ${res.method}` : ''
  const round = res.round ? `, round ${res.round}` : ''
  return `${res.winner} wins${method}${round}`
})
</script>

<template>
  <div class="poster-band">
    <img class="poster-fighter poster-fighter--l" :src="'/motifs/boxer2.png'" alt="" aria-hidden="true">
    <img class="poster-fighter poster-fighter--r" :src="'/motifs/boxer.png'" alt="" aria-hidden="true">
    <div class="kicker" style="text-align:center">
      {{ bout.status === 'completed' ? 'Fight result' : 'Fight preview' }}<template v-if="bout.weightClass"> · {{ bout.weightClass }}</template>
    </div>
    <div class="poster">
      <div class="corner corner--red">
        <span class="corner__tag">Red corner</span>
        <h1 class="pname">
          <NuxtLink v-if="bout.fighterAId" :to="`/fighters/${bout.fighterAId}`">{{ bout.fighter_a }}</NuxtLink>
          <template v-else>{{ bout.fighter_a }}</template>
        </h1>
        <p class="prec">{{ record(fighterA) }}</p>
      </div>
      <div class="pvs"><span>VS</span></div>
      <div class="corner corner--blue">
        <span class="corner__tag">Blue corner</span>
        <h1 class="pname">
          <NuxtLink v-if="bout.fighterBId" :to="`/fighters/${bout.fighterBId}`">{{ bout.fighter_b }}</NuxtLink>
          <template v-else>{{ bout.fighter_b }}</template>
        </h1>
        <p class="prec">{{ record(fighterB) }}</p>
      </div>
    </div>

    <div class="billstrip">
      <span v-if="formatEventDate(bout.eventDate)"><strong>{{ formatEventDate(bout.eventDate) }}</strong></span>
      <span v-if="resultLine" class="billstrip__result"><strong>{{ resultLine }}</strong></span>
      <span class="status" :class="`status--${bout.status}`">{{ bout.status }}</span>
    </div>

    <FightCard :fighter-a="fighterA" :fighter-b="fighterB" />
  </div>
</template>
