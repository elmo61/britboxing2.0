<script setup lang="ts">
// Tale of the tape — opposing bars from a centre spine. Each fighter's bar
// extends outward on a shared scale, so the longer bar genuinely means "more";
// the leading figure is marked gold. Age is shown as plain figures (younger
// isn't "more") with the younger man flagged. All values come from the frozen
// snapshot; a row is shown only when both fighters have that value.
const props = defineProps<{
  fighterA: Record<string, any>
  fighterB: Record<string, any>
}>()

function inchToFtIn(n: number): string { return `${Math.floor(n / 12)}′${n % 12}″` }
function clampPct(p: number): number { return Math.max(6, Math.min(100, p)) }
function koPct(r: any): number | null {
  if (!r?.wins || r?.winsKo == null) return null
  return Math.round((r.winsKo / r.wins) * 100)
}

interface Row {
  label: string
  redDisp: string; blueDisp: string
  redPct: number; bluePct: number
  redEdge: boolean; blueEdge: boolean
}

const rows = computed<Row[]>(() => {
  const a = props.fighterA, b = props.fighterB
  const out: Row[] = []
  const metrics: { label: string; get: (s: any) => number | null; min: number; max: number; disp: (v: number) => string }[] = [
    { label: 'Reach', get: (s) => s.physical?.reachInches ?? null, min: 60, max: 90, disp: (v) => `${v}″` },
    { label: 'Height', get: (s) => s.physical?.heightInches ?? null, min: 60, max: 84, disp: inchToFtIn },
    { label: 'KO ratio', get: (s) => koPct(s.record), min: 0, max: 100, disp: (v) => `${v}%` },
  ]
  for (const m of metrics) {
    const rv = m.get(a), bv = m.get(b)
    if (rv == null || bv == null) continue
    out.push({
      label: m.label,
      redDisp: m.disp(rv), blueDisp: m.disp(bv),
      redPct: clampPct(((rv - m.min) / (m.max - m.min)) * 100),
      bluePct: clampPct(((bv - m.min) / (m.max - m.min)) * 100),
      redEdge: rv > bv, blueEdge: bv > rv,
    })
  }
  return out
})

const ages = computed(() => {
  const r = props.fighterA.physical?.age, b = props.fighterB.physical?.age
  if (r == null || b == null) return null
  return { r, b, redYounger: r < b, blueYounger: b < r }
})

// Animate the bars from zero once mounted (skipped under reduced motion).
const shown = ref(false)
onMounted(() => {
  if (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) { shown.value = true; return }
  requestAnimationFrame(() => setTimeout(() => { shown.value = true }, 60))
})
</script>

<template>
  <div v-if="rows.length || ages" class="tape">
    <p class="tape__head">Tale of the tape</p>

    <div v-for="row in rows" :key="row.label" class="tt">
      <div class="tt__side tt__side--red">
        <span class="tt__val" :class="{ 'is-edge': row.redEdge }">{{ row.redDisp }}</span>
        <div class="tt__track"><div class="tt__fill tt__fill--red" :style="{ width: shown ? row.redPct + '%' : '0%' }"></div></div>
      </div>
      <span class="tt__lab">{{ row.label }}</span>
      <div class="tt__side tt__side--blue">
        <span class="tt__val" :class="{ 'is-edge': row.blueEdge }">{{ row.blueDisp }}</span>
        <div class="tt__track"><div class="tt__fill tt__fill--blue" :style="{ width: shown ? row.bluePct + '%' : '0%' }"></div></div>
      </div>
    </div>

    <div v-if="ages" class="fig">
      <span class="fig__v fig__v--red">{{ ages.r }}</span>
      <span class="tt__lab">Age</span>
      <span class="fig__v fig__v--blue">{{ ages.b }}</span>
    </div>
  </div>
</template>

<style scoped>
.tape { margin: 26px auto 0; max-width: 600px; display: grid; gap: 13px; }
.tape__head {
  font-family: var(--font-cond); font-weight: 600; font-size: .72rem; letter-spacing: .2em;
  text-transform: uppercase; color: var(--muted); text-align: center; margin: 0 0 2px;
}
.tt { display: grid; grid-template-columns: 1fr 92px 1fr; align-items: center; gap: 12px; }
.tt__lab { font-family: var(--font-cond); font-weight: 500; font-size: .62rem; letter-spacing: .16em; text-transform: uppercase; color: var(--muted); text-align: center; }
.tt__side { display: flex; align-items: center; gap: 10px; }
.tt__side--red { justify-content: flex-end; }
.tt__side--blue { justify-content: flex-start; flex-direction: row-reverse; }
.tt__val { font-family: var(--font-cond); font-weight: 600; font-size: 1.02rem; font-variant-numeric: tabular-nums; min-width: 46px; }
.tt__side--red .tt__val { text-align: right; }
.tt__side--blue .tt__val { text-align: left; }
.tt__val.is-edge { color: var(--gold); }
.tt__track { flex: 1; height: 9px; border-radius: 4px; background: #1b1a20; display: flex; overflow: hidden; }
.tt__side--red .tt__track { justify-content: flex-end; }
.tt__fill { height: 100%; border-radius: 4px; transition: width .9s cubic-bezier(.22, .61, .36, 1); }
.tt__fill--red { background: linear-gradient(90deg, var(--red), var(--red-bright)); }
.tt__fill--blue { background: linear-gradient(90deg, #7ba6ef, var(--blue)); }

.fig { display: grid; grid-template-columns: 1fr 92px 1fr; align-items: center; gap: 12px; }
.fig__v { font-family: var(--font-cond); font-weight: 600; font-size: 1.02rem; }
.fig__v--red { text-align: right; }
.fig__v--blue { text-align: left; }
.fig__v small { display: block; font-size: .56rem; letter-spacing: .12em; color: var(--gold); text-transform: uppercase; }

@media (prefers-reduced-motion: reduce) { .tt__fill { transition: none; } }
</style>
