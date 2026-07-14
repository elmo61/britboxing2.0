#!/usr/bin/env node
// Runs after `nuxt generate`. nuxt-og-image names each rendered PNG after the
// props it was given (fighter names, records, status, result...), so a bout's
// image URL changes every time its status/result/date changes — any link
// already shared with the old URL goes dead the next time the site deploys.
// This gives every bout a PERMANENT URL instead: /cards/{bout-slug}.png. The
// image behind that URL still updates in place (new render each deploy), but
// the URL itself never changes and is never dropped, because every bout row
// is permanent and every bout page is always linked from /schedule or
// /results, so it's always re-crawled and re-stabilized on every build.
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

async function main() {
  if (!existsSync(publicDir)) {
    console.error(`[stabilize-share-images] ${publicDir} does not exist — run "nuxt generate" first.`)
    process.exit(1)
  }

  const files = await walkHtml(publicDir)
  const cardsDir = join(publicDir, 'cards')
  await mkdir(cardsDir, { recursive: true })

  const copiedSlugs = new Set()
  let pagesUpdated = 0

  for (const file of files) {
    const slug = boutSlugFromRelPath(relative(publicDir, file))
    if (!slug) continue // not a /fights/... page — uses the static default card, already stable

    const html = await readFile(file, 'utf8')
    const ogMatch = html.match(/<meta property="og:image" content="([^"]+)"/)
    if (!ogMatch) continue
    const dynamicUrl = ogMatch[1]
    if (dynamicUrl.includes('/cards/')) continue // already stabilized (safe to re-run)

    const origin = new URL(dynamicUrl).origin
    const stableUrl = `${origin}/cards/${slug}.png`
    const stableFile = join(cardsDir, `${slug}.png`)

    if (!copiedSlugs.has(slug)) {
      const dynamicFile = join(publicDir, new URL(dynamicUrl).pathname)
      if (!existsSync(dynamicFile)) {
        console.warn(`[stabilize-share-images] source image missing for ${slug}, skipping: ${dynamicFile}`)
        continue
      }
      await copyFile(dynamicFile, stableFile)
      copiedSlugs.add(slug)
    }

    const updated = html
      .replace(/(<meta property="og:image" content=")[^"]+(")/g, `$1${stableUrl}$2`)
      .replace(/(<meta name="twitter:image" content=")[^"]+(")/g, `$1${stableUrl}$2`)
      .replace(/(<meta name="twitter:image:src" content=")[^"]+(")/g, `$1${stableUrl}$2`)
    if (updated !== html) {
      await writeFile(file, updated, 'utf8')
      pagesUpdated++
    }
  }

  console.log(`[stabilize-share-images] gave ${copiedSlugs.size} bout(s) a permanent /cards/ URL across ${pagesUpdated} page(s).`)
}

main()
