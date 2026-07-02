<script setup lang="ts">
const { data: fighters } = await useFetch('/api/fighters')
useHead({ title: 'Fighters | BritBoxing' })

const search = ref('')
const division = ref('')

// Weight classes, lightest to heaviest, for the filter dropdown — built from
// the divisions the roster actually spans so it never shows empty options.
const DIVISION_ORDER = [
  'Minimumweight', 'Light Flyweight', 'Flyweight', 'Super Flyweight',
  'Bantamweight', 'Super Bantamweight', 'Featherweight', 'Super Featherweight',
  'Lightweight', 'Super Lightweight', 'Welterweight', 'Super Welterweight',
  'Middleweight', 'Super Middleweight', 'Light Heavyweight', 'Cruiserweight',
  'Heavyweight',
]
function normDivision(d: string): string {
  const clean = d.replace(/\([^)]*\)/g, '').replace(/-/g, ' ').trim().toLowerCase()
  return DIVISION_ORDER.find((o) => o.toLowerCase() === clean) ?? ''
}
const divisions = computed(() => {
  const present = new Set<string>()
  for (const f of fighters.value ?? []) {
    for (const d of f.divisions ?? []) {
      const n = normDivision(d)
      if (n) present.add(n)
    }
  }
  return DIVISION_ORDER.filter((d) => present.has(d))
})

const filtered = computed(() => (fighters.value ?? []).filter((f: any) => {
  if (search.value && !f.name.toLowerCase().includes(search.value.toLowerCase())) return false
  if (division.value && !(f.divisions ?? []).some((d: string) => normDivision(d) === division.value)) return false
  return true
}))
</script>

<template>
  <div class="wrap wrap--wide">
    <NuxtLink to="/" class="back">← Home</NuxtLink>
    <div class="masthead"><BritLogo /></div>
    <h1>Fighters</h1>

    <div class="filters">
      <input
        v-model="search"
        type="search"
        placeholder="Search fighters…"
        aria-label="Search fighters by name"
      >
      <select v-model="division" aria-label="Filter by weight class">
        <option value="">All weight classes</option>
        <option v-for="d in divisions" :key="d" :value="d">{{ d }}</option>
      </select>
    </div>

    <ul class="roster">
      <li v-for="x in filtered" :key="x.id">
        <NuxtLink :to="`/fighters/${x.id}`">{{ x.name }}</NuxtLink>
        <span class="rec">{{ x.record ?? '—' }}</span>
      </li>
      <li v-if="!filtered.length" class="muted">No fighters match.</li>
    </ul>
  </div>
</template>

<style scoped>
.filters { display: flex; gap: 10px; flex-wrap: wrap; margin: 14px 0 18px; }
.filters input, .filters select {
  background: var(--panel); color: var(--ink); border: 1px solid var(--line);
  border-radius: 5px; padding: 9px 12px; font: inherit; font-size: .92rem;
  min-width: 220px;
}
.filters input:focus, .filters select:focus { outline: none; border-color: var(--gold); }

.roster { list-style: none; padding: 0; margin: 8px 0;
  display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 4px 24px; }
.roster li { display: flex; align-items: baseline; gap: 10px; padding: 8px 0;
  border-bottom: 1px solid var(--line); }
.roster a { color: var(--ink); text-decoration: none; font-weight: 600; flex: 1; }
.roster a:hover { color: var(--gold); }
.rec { color: var(--gold); font-size: .9rem; }
.muted { color: var(--muted); }
</style>
