<script setup lang="ts">
const { data: results } = await useFetch('/api/results')
useHead({ title: 'Results | BritBoxing' })
defaultShareCard() // static default share card

function names(matchup: string): [string, string] {
  const [a, b] = (matchup ?? '').split(' vs ')
  return [a ?? matchup, b ?? '']
}

function resultLine(b: any): string | null {
  if (!b.result?.winner) return null
  const method = b.result.method ? ` by ${b.result.method}` : ''
  const round = b.result.round ? `, round ${b.result.round}` : ''
  return `${b.result.winner} wins${method}${round}`
}
</script>

<template>
  <div class="wrap wrap--wide">
    <h1>Results</h1>
    <p class="lede">Every fight we covered, with how it turned out.</p>

    <ul v-if="results?.length" class="res">
      <li v-for="b in results" :key="b.slug" class="res__row">
        <span class="res__date" :class="{ 'res__date--tbc': !b.eventDate }">{{ formatEventDate(b.eventDate) ?? '' }}</span>
        <div class="res__body">
          <NuxtLink :to="b.reportHref ?? b.href" class="res__matchup">
            {{ names(b.matchup)[0] }} <span class="res__vs">v</span> {{ names(b.matchup)[1] }}
          </NuxtLink>
          <span v-if="resultLine(b)" class="res__result">{{ resultLine(b) }}</span>
          <span class="res__meta">
            <span v-if="b.division" class="res__div">{{ b.division }}</span>
            <span class="status status--completed">result</span>
          </span>
        </div>
        <NuxtLink v-if="b.reportHref" :to="b.href" class="res__hublink">Tale of the tape →</NuxtLink>
      </li>
    </ul>
    <p v-else class="empty">No results yet — check back once the next fight we're covering has happened.</p>
  </div>
</template>

<style scoped>
.lede { color: var(--muted); margin: 4px 0 22px; }
.res { list-style: none; padding: 0; margin: 0; }
.res__row {
  display: grid; grid-template-columns: 110px 1fr auto; align-items: baseline; gap: 16px;
  padding: 16px 6px; border-bottom: 1px solid var(--line);
}
.res__date {
  font-family: var(--font-cond); font-weight: 600; font-size: .82rem; letter-spacing: .06em;
  text-transform: uppercase; color: var(--gold); font-variant-numeric: tabular-nums;
}
.res__date--tbc { color: var(--muted); }
.res__body { display: flex; flex-direction: column; gap: 5px; }
.res__matchup {
  font-family: var(--font-display); text-transform: uppercase; font-size: 1.35rem; line-height: 1;
  color: var(--ink); text-decoration: none;
}
.res__matchup:hover { color: var(--gold); }
.res__vs { color: var(--muted); font-family: var(--font-cond); font-weight: 600; font-size: .7em; }
.res__result {
  font-family: var(--font-cond); font-weight: 600; font-size: .78rem; letter-spacing: .08em;
  text-transform: uppercase; color: var(--gold);
}
.res__meta { display: inline-flex; align-items: center; gap: 10px; }
.res__meta .status { margin-left: 0; }
.res__div { font-family: var(--font-cond); font-size: .72rem; letter-spacing: .08em; text-transform: uppercase; color: var(--muted); }
.res__hublink {
  font-family: var(--font-cond); font-weight: 600; font-size: .72rem; letter-spacing: .06em;
  text-transform: uppercase; color: var(--muted); text-decoration: none; white-space: nowrap;
  align-self: center;
}
.res__hublink:hover { color: var(--gold); }
.empty { color: var(--muted); font-family: var(--font-cond); letter-spacing: .04em; padding: 20px 2px; }
@media (max-width: 560px) {
  .res__row { grid-template-columns: 1fr; gap: 6px; }
  .res__hublink { justify-self: start; }
}
</style>
