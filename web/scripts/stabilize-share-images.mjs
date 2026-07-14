#!/usr/bin/env node
// Runs after `nuxt generate`. nuxt-og-image names each rendered PNG after the
// props it was given (fighter names, records, status, result...), so a bout's
// image URL changes every time its status/result/date changes — any link
// already shared with the old URL goes dead the next time the site deploys.
// This gives every bout a PERMANENT URL instead: /cards/{bout-slug}.png (plus
// -square.png / -small.png for the alternate sizes each fight page also
// requests). The image behind each URL still updates in place (new render
// each deploy), but the URL itself never changes and is never dropped,
// because every bout row is permanent and every bout page is always linked
// from /schedule or /results, so it's always re-crawled and re-stabilized on
// every build.
//
// Each fight page carries THREE og:image entries in document order (hero,
// square, small — see pages/fights/[bout]/index.vue) — valid per the Open
// Graph spec (multiple og:image is explicitly supported; platforms that only
// want one just use the first / best-fit by the accompanying width/height).
// Distinguish them by their declared og:image:width.
import { readdir, readFile, writeFile, copyFile, mkdir } from 'node:fs/promises'
import { existsSync } from 'node:fs'
import { join, dirname, relative, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const publicDir = join(dirname(fileURLToPath(import.meta.url)), '..', '.output', 'public')

async function walkHtml(dir) {
  const out = []
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name)
    if (entry.isDirectory()) out.push(...await walkHtml(full))
    else if (entry.name === 'index.html') out.push(full)
  }
  return out
}

// .../public/fights/<slug>/index.html or .../public/fights/<slug>/<article>/index.html
function boutSlugFromRelPath(relPath) {
  const parts = relPath.split(sep).join('/').split('/')
  const idx = parts.indexOf('fights')
  return idx === -1 ? null : (parts[idx + 1] ?? null)
}

// Declared og:image:width -> stable filename suffix.
const SUFFIX_BY_WIDTH = { 1200: '', 1080: '-square', 600: '-small' }

async function main() {
  if (!existsSync(publicDir)) {
    console.error(`[stabilize-share-images] ${publicDir} does not exist — run "nuxt generate" first.`)
    process.exit(1)
  }

  const files = await walkHtml(publicDir)
  const cardsDir = join(publicDir, 'cards')
  await mkdir(cardsDir, { recursive: true })

  const copiedTargets = new Set() // "{slug}{suffix}" already copied this run
  let pagesUpdated = 0
  let boutsSeen = new Set()

  for (const file of files) {
    const slug = boutSlugFromRelPath(relative(publicDir, file))
    if (!slug) continue // not a /fights/... page — uses the static default card, already stable

    let html = await readFile(file, 'utf8')
    const urls = [...html.matchAll(/<meta property="og:image" content="([^"]+)">/g)].map(m => m[1])
    const widths = [...html.matchAll(/<meta property="og:image:width" content="(\d+)">/g)].map(m => Number(m[1]))
    if (!urls.length) continue

    boutsSeen.add(slug)
    let changed = false

    for (let i = 0; i < urls.length; i++) {
      const dynamicUrl = urls[i]
      if (dynamicUrl.includes('/cards/')) continue // already stabilized (safe to re-run)

      const suffix = SUFFIX_BY_WIDTH[widths[i]]
      if (suffix === undefined) {
        console.warn(`[stabilize-share-images] unrecognized og:image width ${widths[i]} for ${slug}, leaving as-is: ${dynamicUrl}`)
        continue
      }

      const origin = new URL(dynamicUrl).origin
      const stableUrl = `${origin}/cards/${slug}${suffix}.png`
      const target = `${slug}${suffix}`

      if (!copiedTargets.has(target)) {
        const dynamicFile = join(publicDir, new URL(dynamicUrl).pathname)
        if (!existsSync(dynamicFile)) {
          console.warn(`[stabilize-share-images] source image missing for ${target}, skipping: ${dynamicFile}`)
          continue
        }
        await copyFile(dynamicFile, join(cardsDir, `${target}.png`))
        copiedTargets.add(target)
      }

      // Global replace: catches every og:image occurrence of this exact URL
      // plus twitter:image/twitter:image:src (which always mirror the hero).
      const escaped = dynamicUrl.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
      html = html.replace(new RegExp(escaped, 'g'), stableUrl)
      changed = true
    }

    if (changed) {
      await writeFile(file, html, 'utf8')
      pagesUpdated++
    }
  }

  console.log(`[stabilize-share-images] gave ${boutsSeen.size} bout(s) permanent /cards/ URLs (${copiedTargets.size} image files) across ${pagesUpdated} page(s).`)
}

main()
