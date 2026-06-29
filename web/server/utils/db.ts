// Filesystem access to the JSON "database" in ../data.
// This is the seam: today it reads local JSON, later these helpers (or the API
// routes that call them) point at the real C# API instead. Nothing in the Vue
// pages knows where the data comes from — they only call /api/fights.
import { promises as fs } from 'node:fs'
import { join } from 'node:path'

function dataDir(): string {
  return useRuntimeConfig().dataDir as string
}

async function readJson<T = any>(sub: string, slug: string): Promise<T> {
  const path = join(dataDir(), sub, `${slug}.json`)
  return JSON.parse(await fs.readFile(path, 'utf-8'))
}

/** Slugs that have a published article (the ones the site should show). */
export async function listSlugs(): Promise<string[]> {
  const dir = join(dataDir(), 'articles')
  const files = await fs.readdir(dir).catch(() => [] as string[])
  return files.filter(f => f.endsWith('.json')).map(f => f.replace(/\.json$/, '')).sort()
}

export interface Fight {
  slug: string
  article: Record<string, any>
  bout: Record<string, any>
  fighterA: Record<string, any>
  fighterB: Record<string, any>
}

/** Full fight: article + bout context + both fighter snapshots. */
export async function getFight(slug: string): Promise<Fight> {
  const [article, pkg] = await Promise.all([
    readJson('articles', slug),
    readJson('packages', slug),
  ])
  return {
    slug,
    article,
    bout: pkg.bout,
    fighterA: pkg.fighter_a_snapshot,
    fighterB: pkg.fighter_b_snapshot,
  }
}

// --- Fighters (the internal-linking layer) ------------------------------- //

export async function listFighterIds(): Promise<string[]> {
  const dir = join(dataDir(), 'fighters')
  const files = await fs.readdir(dir).catch(() => [] as string[])
  return files.filter(f => f.endsWith('.json')).map(f => f.replace(/\.json$/, '')).sort()
}

/** A fighter record plus the bouts they appear in, each resolved to the
 *  opponent + division so a fighter page can link out to every fight. */
export async function getFighterProfile(id: string) {
  const fighter = await readJson('fighters', id)
  const bouts = await Promise.all(
    (fighter.bouts ?? []).map(async (slug: string) => {
      try {
        const pkg = await readJson('packages', slug)
        const isA = pkg.bout.fighterAId === id
        return {
          slug,
          opponentName: isA ? pkg.bout.fighter_b : pkg.bout.fighter_a,
          opponentId: isA ? pkg.bout.fighterBId : pkg.bout.fighterAId,
          division: pkg.bout.weightClass ?? '',
        }
      } catch {
        return null
      }
    }),
  )
  return { fighter, bouts: bouts.filter(Boolean) }
}
