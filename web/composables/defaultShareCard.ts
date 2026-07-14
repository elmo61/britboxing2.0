// Share-preview meta for pages that use the static branded card rather than
// a per-fight generated one (home, schedule, fighter pages). The absolute URL
// comes from site.url in nuxt.config, so flipping the canonical domain there
// updates every page.
export function defaultShareCard() {
  const site = useSiteConfig()
  const img = `${site.url}/og-default.png`
  useSeoMeta({
    ogImage: img,
    twitterCard: 'summary_large_image',
    twitterImage: img,
  })
}
