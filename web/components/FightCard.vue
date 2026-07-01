<script setup lang="ts">
// Head-to-head "tale of the tape" — the data-driven core of every preview.
// Fighter A = red corner, Fighter B = blue corner. Names link to fighter pages.
const props = defineProps<{
  fighterA: Record<string, any>
  fighterB: Record<string, any>
  hrefA?: string
  hrefB?: string
}>()

const pillClass: Record<string, string> = { W: 'w', L: 'l', D: 'd', N: 'n' }

function koPct(r: any): number | null {
  if (!r?.wins || r?.winsKo == null) return null
  return Math.round((r.winsKo / r.wins) * 100)
}
function recordLine(r: any): string {
  if (r.wins == null) return '—'
  const base = `${r.wins}-${r.losses ?? 0}-${r.draws ?? 0}`
  return r.noContests ? `${base} (${r.noContests} NC)` : base
}
function height(i: number | null): string {
  return i ? `${Math.floor(i / 12)}′${i % 12}″` : '—'
}

interface Col {
  name: string; record: string; ko: string; stance: string
  age: string; height: string; reach: string; last5: string[]; unverified: boolean
}
function toCol(s: any): Col {
  const r = s.record, ph = s.physical, fm = s.form
  const pct = koPct(r)
  return {
    name: s._meta.name,
    record: recordLine(r),
    ko: r.winsKo == null ? '—' : `${r.winsKo}${pct != null ? ` (${pct}%)` : ''}`,
    stance: ph.stance ?? '—',
    age: ph.age ?? '—',
    height: height(ph.heightInches),
    reach: ph.reachInches ? `${ph.reachInches}″` : '—',
    last5: fm.last5 ?? [],
    unverified: !!fm._unverified,
  }
}
const a = computed(() => toCol(props.fighterA))
const b = computed(() => toCol(props.fighterB))
</script>

<template>
  <div class="bill">
    <div class="names">
      <div class="fighter">
        <div class="corner red">● Red corner</div>
        <h3 class="fname">
          <NuxtLink v-if="hrefA" :to="hrefA">{{ a.name }}</NuxtLink>
          <template v-else>{{ a.name }}</template>
        </h3>
        <div class="rec">{{ a.record }}</div>
      </div>
      <div class="vs">VS</div>
      <div class="fighter right">
        <div class="corner blue">Blue corner ●</div>
        <h3 class="fname">
          <NuxtLink v-if="hrefB" :to="hrefB">{{ b.name }}</NuxtLink>
          <template v-else>{{ b.name }}</template>
        </h3>
        <div class="rec">{{ b.record }}</div>
      </div>
    </div>

    <div class="tape">
      <div class="row"><span class="v l">{{ a.ko }}</span><span class="lab">KO wins</span><span class="v r">{{ b.ko }}</span></div>
      <div class="row"><span class="v l">{{ a.stance }}</span><span class="lab">Stance</span><span class="v r">{{ b.stance }}</span></div>
      <div class="row"><span class="v l">{{ a.age }}</span><span class="lab">Age</span><span class="v r">{{ b.age }}</span></div>
      <div class="row"><span class="v l">{{ a.height }}</span><span class="lab">Height</span><span class="v r">{{ b.height }}</span></div>
      <div class="row"><span class="v l">{{ a.reach }}</span><span class="lab">Reach</span><span class="v r">{{ b.reach }}</span></div>
      <div class="row">
        <div class="pills l">
          <span v-for="(r, j) in a.last5" :key="j" class="pill" :class="pillClass[r[0]] || 'n'">{{ r }}</span>
          <span v-if="!a.last5.length" class="muted">—</span>
        </div>
        <span class="lab">Last 5<span v-if="a.unverified || b.unverified" class="flag">unverified</span></span>
        <div class="pills r">
          <span v-for="(r, j) in b.last5" :key="j" class="pill" :class="pillClass[r[0]] || 'n'">{{ r }}</span>
          <span v-if="!b.last5.length" class="muted">—</span>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.bill {
  border: 1px solid var(--line);
  background:
    repeating-linear-gradient(0deg, #101015, #101015 2px, #0d0d12 2px, #0d0d12 4px);
  border-radius: 6px;
  padding: 26px 24px 28px;
  margin: 24px 0;
}
.names { display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; gap: 12px; }
.fighter { text-align: center; }
.corner {
  font-family: var(--font-cond); font-weight: 600; font-size: .68rem;
  letter-spacing: .22em; text-transform: uppercase;
}
.corner.red { color: var(--red); }
.corner.blue { color: var(--blue); }
.fname {
  font-family: var(--font-display); font-weight: 400; text-transform: uppercase;
  font-size: 2.2rem; line-height: .96; margin: 8px 0 6px;
}
.fname a { color: inherit; text-decoration: none; }
.fname a:hover { color: var(--gold); }
.rec { font-family: var(--font-cond); font-weight: 600; font-size: 1.05rem; color: var(--gold); letter-spacing: .02em; }
.vs { font-family: var(--font-display); font-size: 1.5rem; color: var(--ink); opacity: .45; }

.tape { margin-top: 24px; border-top: 1px solid var(--line); }
.row {
  display: grid; grid-template-columns: 1fr auto 1fr; align-items: center;
  border-bottom: 1px solid var(--line); padding: 9px 0;
}
.v { font-family: var(--font-cond); font-weight: 600; font-size: 1.1rem; }
.v.l { text-align: right; padding-right: 18px; }
.v.r { text-align: left; padding-left: 18px; }
.lab {
  font-family: var(--font-cond); font-weight: 500; font-size: .66rem;
  letter-spacing: .2em; text-transform: uppercase; color: var(--muted); text-align: center;
}
.flag {
  display: inline-block; font-size: .58rem; color: var(--gold); letter-spacing: .04em;
  border: 1px solid var(--gold); border-radius: 3px; padding: 0 4px; margin-left: 6px;
}
.pills { display: flex; gap: 4px; flex-wrap: wrap; min-width: 0; }
.pills.l { justify-content: flex-end; padding-right: 18px; }
.pills.r { justify-content: flex-start; padding-left: 18px; }
.pill { font-family: var(--font-cond); font-size: .64rem; font-weight: 600; padding: 2px 6px; color: #fff; }
.pill.w { background: var(--win); }
.pill.l { background: var(--loss); }
.pill.d { background: var(--draw); }
.pill.n { background: #444; }
.muted { color: var(--muted); }

/* narrow screens: tighter tape so the last-5 pills never force the page wide */
@media (max-width: 560px) {
  .bill { padding: 20px 12px 22px; }
  .fname { font-size: 1.45rem; }
  .rec { font-size: .92rem; }
  .v { font-size: .95rem; }
  .v.l { padding-right: 10px; }
  .v.r { padding-left: 10px; }
  .lab { font-size: .58rem; letter-spacing: .12em; }
  .pills.l { padding-right: 8px; }
  .pills.r { padding-left: 8px; }
  .pill { font-size: .56rem; padding: 2px 4px; }
}
</style>
