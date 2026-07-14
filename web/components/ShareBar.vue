<script setup lang="ts">
// Visible share row for fight hub + article pages. The share-card PNG (built
// at generate time by nuxt-og-image) already sits in og:image/twitter:image
// meta tags for platforms that read link previews automatically; this makes
// the same image a real, clickable link on the page itself, plus one-tap
// share intents so a reader doesn't have to know link-preview mechanics exist.
const props = defineProps<{ title: string, boutSlug: string }>()

const route = useRoute()
// useRequestURL().origin reflects whoever's actually asking — during static
// prerendering that's Nitro's own internal crawler (http://localhost:PORT),
// not the public site, so it would bake wrong URLs into the static HTML.
// site.url is deterministic regardless of who's rendering.
const origin = useSiteConfig().url
const pageUrl = computed(() => origin + route.path)

// The build's stabilize-share-images.mjs step rewrites every fight/article
// page's og:image to exactly this permanent URL (see that script for why:
// nuxt-og-image's own filename is props-derived and changes whenever the
// bout's status/result changes). Compute it directly rather than reading the
// og:image meta tag — Unhead can reactively re-apply the page's original,
// unstable URL to the live DOM on hydration even though the static HTML any
// crawler actually reads was already correctly rewritten.
const cardImageUrl = computed(() => `${origin}/cards/${props.boutSlug}.png`)
const cardImageSquareUrl = computed(() => `${origin}/cards/${props.boutSlug}-square.png`)
const cardImageSmallUrl = computed(() => `${origin}/cards/${props.boutSlug}-small.png`)

const xHref = computed(() =>
  `https://twitter.com/intent/tweet?text=${encodeURIComponent(props.title)}&url=${encodeURIComponent(pageUrl.value)}`)
const waHref = computed(() =>
  `https://wa.me/?text=${encodeURIComponent(`${props.title} ${pageUrl.value}`)}`)

const copyState = ref<'idle' | 'copied' | 'failed'>('idle')
async function copyLink() {
  try {
    await navigator.clipboard.writeText(pageUrl.value)
    copyState.value = 'copied'
  } catch {
    // Clipboard API can be blocked (permissions, insecure context, automation).
    // Fall back to a hidden-textarea copy rather than leaving the click inert.
    try {
      const ta = document.createElement('textarea')
      ta.value = pageUrl.value
      ta.style.position = 'fixed'
      ta.style.opacity = '0'
      document.body.appendChild(ta)
      ta.select()
      const ok = document.execCommand('copy')
      document.body.removeChild(ta)
      copyState.value = ok ? 'copied' : 'failed'
    } catch {
      copyState.value = 'failed'
    }
  }
  setTimeout(() => { copyState.value = 'idle' }, 1800)
}
</script>

<template>
  <div class="sharebar">
    <span class="sharebar__label">Share</span>
    <a :href="xHref" target="_blank" rel="noopener noreferrer" class="sharebar__btn">X</a>
    <a :href="waHref" target="_blank" rel="noopener noreferrer" class="sharebar__btn">WhatsApp</a>
    <button type="button" class="sharebar__btn" @click="copyLink">
      {{ copyState === 'copied' ? 'Copied!' : copyState === 'failed' ? 'Copy failed' : 'Copy link' }}
    </button>
    <a :href="cardImageUrl" target="_blank" rel="noopener noreferrer" class="sharebar__btn sharebar__btn--image">Share image ↗</a>
    <a :href="cardImageSquareUrl" target="_blank" rel="noopener noreferrer" class="sharebar__btn sharebar__btn--image">Square ↗</a>
    <a :href="cardImageSmallUrl" target="_blank" rel="noopener noreferrer" class="sharebar__btn sharebar__btn--image">Small ↗</a>
  </div>
</template>

<style scoped>
.sharebar {
  display: flex; flex-wrap: wrap; align-items: center; gap: 8px;
  margin: 18px 0 0;
}
.sharebar__label {
  font-family: var(--font-cond); font-weight: 600; font-size: .68rem; letter-spacing: .16em;
  text-transform: uppercase; color: var(--muted); margin-right: 2px;
}
.sharebar__btn {
  font-family: var(--font-cond); font-weight: 600; font-size: .72rem; letter-spacing: .06em;
  text-transform: uppercase; color: var(--ink); text-decoration: none;
  border: 1px solid var(--line-2); border-radius: 3px; padding: 5px 11px;
  background: transparent; cursor: pointer; transition: border-color .14s, color .14s;
}
.sharebar__btn:hover { border-color: var(--gold); color: var(--gold); }
.sharebar__btn--image { color: var(--gold); border-color: var(--gold); }
</style>
