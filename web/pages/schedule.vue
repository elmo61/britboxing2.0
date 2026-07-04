<script setup lang="ts">
const { data: bouts } = await useFetch('/api/schedule')
useHead({ title: 'Schedule | BritBoxing' })

function names(matchup: string): [string, string] {
  const [a, b] = (matchup ?? '').split(' vs ')
  return [a ?? matchup, b ?? '']
}
</script>

<template>
  <div class="wrap wrap--wide">
    <h1>Schedule</h1>
    <p class="lede">Upcoming fights by date; fights without a confirmed date yet are listed below.</p>

    <ul v-if="bouts?.length" class="sched">
      <li v-for="b in bouts" :key="b.slug" class="sched__row">
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
.empty { color: var(--muted); font-family: var(--font-cond); letter-spacing: .04em; padding: 20px 2px; }
@media (max-width: 560px) {
  .sched__link { grid-template-columns: 1fr; gap: 4px; }
}
</style>
