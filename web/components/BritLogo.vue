<script setup lang="ts">
// BritBoxing wordmark + accurate Union Jack (counterchanged saltire).
withDefaults(defineProps<{ size?: number }>(), { size: 44 })
</script>

<template>
  <NuxtLink to="/" class="britlogo" aria-label="BritBoxing home">
    <svg class="uj" :style="{ width: size + 'px', height: (size / 2) + 'px' }"
         viewBox="0 0 60 30" xmlns="http://www.w3.org/2000/svg" aria-hidden="true">
      <clipPath id="ujclip"><path d="M0,0 v30 h60 v-30 z" /></clipPath>
      <clipPath id="ujcc"><path d="M30,15 h30 v15 zv15 h-30 zh-30 v-15 zv-15 h30 z" /></clipPath>
      <g clip-path="url(#ujclip)">
        <rect width="60" height="30" fill="#012169" />
        <path d="M0,0 L60,30 M60,0 L0,30" stroke="#fff" stroke-width="6" />
        <path d="M0,0 L60,30 M60,0 L0,30" clip-path="url(#ujcc)" stroke="#cf142b" stroke-width="4" />
        <path d="M30,0 v30 M0,15 h60" stroke="#fff" stroke-width="10" />
        <path d="M30,0 v30 M0,15 h60" stroke="#cf142b" stroke-width="6" />
      </g>
    </svg>
    <span class="wm" :style="{ fontSize: (size * 0.045) + 'rem' }"><b>BRIT</b>BOXING</span>
  </NuxtLink>
</template>

<style scoped>
.britlogo { display: inline-flex; align-items: center; gap: 12px; text-decoration: none; }
.uj { flex: none; border: 1px solid #000; display: block; }
.wm {
  font-family: var(--font-display); letter-spacing: .02em; text-transform: uppercase;
  color: var(--ink); line-height: 1;
}
.wm b { color: var(--red); font-weight: 400; }
</style>
