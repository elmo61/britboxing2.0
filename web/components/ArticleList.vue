<script setup lang="ts">
// Coverage history for a bout: each article's title, posted date and the
// fight status at the time. Used on the bout hub and under each article.
defineProps<{
  articles: any[]
  boutSlug: string
  currentSlug?: string
  heading?: string
}>()
</script>

<template>
  <section v-if="articles.length" class="coverage">
    <h2 class="coverage__head">{{ heading ?? 'Coverage' }}</h2>
    <ul class="coverage__list">
      <li v-for="a in articles" :key="a.slug" class="coverage__row" :class="{ 'is-current': a.slug === currentSlug }">
        <NuxtLink :to="`/fights/${boutSlug}/${a.slug}`" class="coverage__title">{{ a.title }}</NuxtLink>
        <span class="coverage__meta">
          <span v-if="a.status" class="status" :class="`status--${a.status}`">{{ a.status }}</span>
          <span v-if="formatPostedAt(a.published_at)" class="coverage__date">{{ formatPostedAt(a.published_at) }}</span>
        </span>
      </li>
    </ul>
  </section>
</template>

<style scoped>
.coverage { margin: clamp(30px, 5vw, 44px) 0 0; }
.coverage__head {
  font-family: var(--font-cond); font-weight: 600; font-size: .78rem; letter-spacing: .18em;
  text-transform: uppercase; color: var(--muted); margin: 0 0 10px; padding-bottom: 10px;
  border-bottom: 1px solid var(--line);
}
.coverage__list { list-style: none; padding: 0; margin: 0; }
.coverage__row {
  display: flex; align-items: baseline; justify-content: space-between; gap: 12px;
  padding: 11px 0; border-bottom: 1px solid var(--line);
}
.coverage__row.is-current { opacity: .55; }
.coverage__title { color: var(--ink); text-decoration: none; font-weight: 500; }
.coverage__title:hover { color: var(--gold); }
.coverage__row.is-current .coverage__title { pointer-events: none; }
.coverage__meta { display: inline-flex; align-items: center; gap: 10px; flex-shrink: 0; }
.coverage__meta .status { margin-left: 0; }
.coverage__date {
  font-family: var(--font-cond); font-size: .72rem; letter-spacing: .06em;
  text-transform: uppercase; color: var(--muted); white-space: nowrap;
}
</style>
