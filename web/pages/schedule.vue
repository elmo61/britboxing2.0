<script setup lang="ts">
const { data: bouts } = await useFetch('/api/schedule')
useHead({ title: 'Schedule | BritBoxing' })
defaultShareCard() // static default share card

// Completed fights drop out of the upcoming list into "Recent results",
// newest result first.
const upcoming = computed(() => (bouts.value ?? []).filter((b: any) => b.status !== 'completed'))
const results = computed(() => (bouts.value ?? [])
  .filter((b: any) => b.status === 'completed')
  .slice()
  .sort((x: any, y: any) => (y.eventDate ?? '').localeCompare(x.eventDate ?? '')))

function names(matchup: string): [string, string] {
  const [a, b] = (matchup ?? '').split(' vs ')
  return [a ?? matchup, b ?? '']
}

function resultLine(b: any): string | null {
  if (!b.result?.winner) return null
  const method = b.result.method ? ` ${b.result.method}` : ''
  const round = b.result.round ? ` R${b.result.round}` : ''
  return `${b.result.winner}${method}${round}`
}
</script>

<template>
  <div class="wrap wrap--wide">
    <h1>Schedule</h1>
    <p class="lede">Upcoming fights by date; fights without a confirmed date yet are listed below.</p>

    <ul v-if="upcoming.length" class="sched">
      <li v-for="b in upcoming" :key="b.slug" class="sched__row">
        <NuxtLink :to="b.href" class="sched__link">
          <span class="sched__date" :class="{ 'sched__date--tbc': !b.eventDate }">{{ formatEventDate(b.eventDate) ?? 'TBC' }}</span>
          <span class="sched__matchup">{{ names(b.matchup)[0] }} <span class="sched__vs">v</span> {{ names(b.matchup)[1] }}</span>
          <span class="sched__meta">
            <span v-if="b.division" class="sched__div">{{ b.division }}</span>
            <span class="status" :class="`status--${b.status}`">{{ b.status }}</span>
          </span>
        </NuxtLink>
      </li>
    </ul>
    <p v-else class="empty">No dated fights yet — dates fill in as announcements firm up.</p>

    <template v-if="results.length">
      <h2 class="sched__results-head">Recent results</h2>
      <ul class="sched">
        <li v-for="b in results" :key="b.slug" class="sched__row">
          <NuxtLink :to="b.href" class="sched__link">
            <span class="sched__date" :class="{ 'sched__date--tbc': !b.eventDate }">{{ formatEventDate(b.eventDate) ?? '' }}</span>
            <span class="sched__matchup">
              {{ names(b.matchup)[0] }} <span class="sched__vs">v</span> {{ names(b.matchup)[1] }}
              <span v-if="resultLine(b)" class="sched__result">{{ resultLine(b) }}</span>
            </span>
            <span class="sched__meta">
              <span v-if="b.division" class="sched__div">{{ b.division }}</span>
              <span class="status status--completed">result</span>
            </span>
          </NuxtLink>
        </li>
      </ul>
    </template>
  </div>
</template>

<style scoped>
.lede { color: var(--muted); margin: 4px 0 22px; }
.sched { list-style: none; padding: 0; margin: 0; }
.sched__row { border-bottom: 1px solid var(--line); }
.sched__link {
  display: grid; grid-template-columns: 130px 1fr auto; align-items: center; gap: 16px;
  padding: 14px 6px; text-decoration: none; color: inherit; transition: background .14s, padding-left .14s;
}
.sched__link:hover { background: var(--panel); padding-left: 12px; }
.sched__date {
  font-family: var(--font-cond); font-weight: 600; font-size: .82rem; letter-spacing: .06em;
  text-transform: uppercase; color: var(--gold); font-variant-numeric: tabular-nums;
}
.sched__date--tbc { color: var(--muted); }
.sched__matchup { font-family: var(--font-display); text-transform: uppercase; font-size: 1.35rem; line-height: 1; }
.sched__vs { color: var(--muted); font-family: var(--font-cond); font-weight: 600; font-size: .7em; }
.sched__meta { display: inline-flex; align-items: center; gap: 10px; }
.sched__meta .status { margin-left: 0; }
.sched__div { font-family: var(--font-cond); font-size: .72rem; letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }
.sched__results-head {
  font-family: var(--font-cond); font-weight: 600; font-size: .78rem; letter-spacing: .18em;
  text-transform: uppercase; color: var(--muted); margin: 34px 0 6px; padding-bottom: 10px;
  border-bottom: 1px solid var(--line);
}
.sched__result {
  display: block; font-family: var(--font-cond); font-weight: 600; font-size: .74rem;
  letter-spacing: .1em; text-transform: uppercase; color: var(--gold); margin-top: 4px;
}
.empty { color: var(--muted); font-family: var(--font-cond); letter-spacing: .04em; padding: 20px 2px; }
@media (max-width: 560px) {
  .sched__link { grid-template-columns: 1fr; gap: 4px; }
}
</style>
