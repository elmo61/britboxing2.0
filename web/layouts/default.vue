<script setup lang="ts">
// Shared shell: a collapsing sticky header (big banner that shrinks to a slim
// bar on scroll) and a footer. Every page renders inside this.
const route = useRoute()
const collapsed = ref(false)

const latestActive = computed(() => route.path === '/' || route.path.startsWith('/fights'))
const scheduleActive = computed(() => route.path.startsWith('/schedule'))
const resultsActive = computed(() => route.path.startsWith('/results'))
const fightersActive = computed(() => route.path.startsWith('/fighters'))

let onScroll: (() => void) | null = null
onMounted(() => {
  let ticking = false
  onScroll = () => {
    if (ticking) return
    ticking = true
    requestAnimationFrame(() => {
      collapsed.value = (window.scrollY || document.documentElement.scrollTop) > 24
      ticking = false
    })
  }
  window.addEventListener('scroll', onScroll, { passive: true })
  onScroll()
})
onBeforeUnmount(() => { if (onScroll) window.removeEventListener('scroll', onScroll) })
</script>

<template>
  <div>
    <header class="site-head" :class="{ 'is-collapsed': collapsed }">
      <div class="site-head__inner">
        <div class="brand">
          <NuxtLink to="/" class="brand__mark" aria-label="BritBoxing home">BRIT<span>BOXING</span></NuxtLink>
          <p class="brand__tagline">The big fights, previewed by the numbers</p>
        </div>
        <nav class="mainnav">
          <NuxtLink to="/" :class="{ 'is-active': latestActive }">Latest</NuxtLink>
          <NuxtLink to="/schedule" :class="{ 'is-active': scheduleActive }">Schedule</NuxtLink>
          <NuxtLink to="/results" :class="{ 'is-active': resultsActive }">Results</NuxtLink>
          <NuxtLink to="/fighters" :class="{ 'is-active': fightersActive }">Fighters</NuxtLink>
        </nav>
      </div>
    </header>

    <main>
      <slot />
    </main>

    <footer class="site-foot">
      <div class="site-foot__inner">
        <p class="site-foot__note">
          Data-driven boxing previews. Fighter records derived from English Wikipedia,
          <a href="https://creativecommons.org/licenses/by-sa/4.0/">CC&nbsp;BY-SA&nbsp;4.0</a>.
        </p>
        <nav class="site-foot__nav">
          <NuxtLink to="/">Previews</NuxtLink>
          <NuxtLink to="/results">Results</NuxtLink>
          <NuxtLink to="/fighters">Fighters</NuxtLink>
        </nav>
      </div>
    </footer>
  </div>
</template>
