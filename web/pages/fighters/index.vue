<script setup lang="ts">
const { data: fighters } = await useFetch('/api/fighters')
useHead({ title: 'Fighters | BritBoxing' })
</script>

<template>
  <div class="wrap wrap--wide">
    <NuxtLink to="/" class="back">← Home</NuxtLink>
    <div class="masthead"><BritLogo /></div>
    <h1>Fighters</h1>
    <p class="lede">{{ fighters?.length ?? 0 }} fighters across the previews.</p>

    <ul class="roster">
      <li v-for="x in fighters" :key="x.id">
        <NuxtLink :to="`/fighters/${x.id}`">{{ x.name }}</NuxtLink>
        <span class="rec">{{ x.record ?? '—' }}</span>
        <span class="cnt">{{ x.boutCount }} fight<span v-if="x.boutCount !== 1">s</span></span>
      </li>
    </ul>
  </div>
</template>

<style scoped>
.roster { list-style: none; padding: 0; margin: 8px 0;
  display: grid; grid-template-columns: repeat(auto-fill, minmax(280px, 1fr)); gap: 4px 24px; }
.roster li { display: flex; align-items: baseline; gap: 10px; padding: 8px 0;
  border-bottom: 1px solid var(--line); }
.roster a { color: var(--ink); text-decoration: none; font-weight: 600; flex: 1; }
.roster a:hover { color: var(--gold); }
.rec { color: var(--gold); font-size: .9rem; }
.cnt { color: var(--muted); font-size: .8rem; }
</style>
