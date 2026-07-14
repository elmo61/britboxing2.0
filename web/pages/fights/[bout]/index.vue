<script setup lang="ts">
// Bout hub: the fight itself (poster + tale-of-the-tape) and the full list of
// articles written about it. No article body here — this is the fight's home.
const route = useRoute()
const bout = route.params.bout as string

const { data: f, error } = await useFetch(`/api/fights/${bout}`)
if (error.value || !f.value) {
  throw createError({ statusCode: 404, statusMessage: 'Fight not found', fatal: true })
}

const wikiSources = [
  { label: `${f.value.bout.fighter_a} (Wikipedia)`, url: f.value.fighterA?._meta?.source },
  { label: `${f.value.bout.fighter_b} (Wikipedia)`, url: f.value.fighterB?._meta?.source },
]

useHead({ title: `${f.value.bout.fighter_a} vs ${f.value.bout.fighter_b} | BritBoxing` })

// Build-time share cards: hero (1200x630, the primary og:image/twitter:image),
// plus square and small alternates — additional valid og:image entries with
// their own width/height, ignored by platforms that only want one.
defineOgImage('FightCard', fightCardProps(f.value))
defineOgImage('FightCardSquare', fightCardProps(f.value), { key: 'square', width: 1080, height: 1080 })
defineOgImage('FightCardSmall', fightCardProps(f.value), { key: 'small', width: 600, height: 315 })
</script>

<template>
  <div class="wrap" v-if="f">
    <NuxtLink to="/" class="back">← Latest</NuxtLink>

    <FightHeader :bout="f.bout" :fighter-a="f.fighterA" :fighter-b="f.fighterB" />
    <ShareBar :title="`${f.bout.fighter_a} vs ${f.bout.fighter_b}`" :bout-slug="f.slug" />

    <ArticleList :articles="f.articles" :bout-slug="f.slug" heading="Coverage of this fight" />

    <footer class="attribution">
      <p>
        <strong>Sources &amp; attribution.</strong>
        Fighter records derived from English Wikipedia
        (<template v-for="(s, i) in wikiSources" :key="i"><a v-if="s.url" :href="s.url">{{ s.label }}</a><template v-if="i === 0 && s.url"> · </template></template>),
        licensed <a href="https://creativecommons.org/licenses/by-sa/4.0/">CC&nbsp;BY-SA&nbsp;4.0</a>.
      </p>
    </footer>
  </div>
</template>
