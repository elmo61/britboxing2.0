<script setup lang="ts">
const { data: fights } = await useFetch('/api/fights')

useHead({
  title: 'BritBoxing · fight previews',
  meta: [{ name: 'description', content: 'Data-driven British boxing fight previews.' }],
})
</script>

<template>
  <div class="wrap wrap--wide">
    <div class="masthead">
      <BritLogo />
      <span class="tag">The British fight bill</span>
    </div>
    <h1>Fight previews</h1>
    <p class="lede">
      {{ fights?.length ?? 0 }} previews. Click through to compare how each reads.
      <NuxtLink to="/fighters" class="navlink">Browse fighters →</NuxtLink>
    </p>

    <div class="grid">
      <NuxtLink
        v-for="f in fights"
        :key="f.slug"
        class="card"
        :to="`/fights/${f.slug}`"
      >
        <div class="kicker">
          {{ f.division }}<template v-if="f.division && formatEventDate(f.eventDate)"> · </template><template v-if="formatEventDate(f.eventDate)">{{ formatEventDate(f.eventDate) }}</template>
        </div>
        <h2>{{ f.title }}</h2>
        <div class="matchup">{{ f.matchup }}</div>
        <p>{{ f.summary }}</p>
        <div v-if="formatPostedAt(f.postedAt)" class="posted">Posted {{ formatPostedAt(f.postedAt) }}</div>
      </NuxtLink>
    </div>
  </div>
</template>
