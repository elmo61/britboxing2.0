// Same 3 metrics + scale as the on-page tale-of-the-tape (components/FightCard.vue)
// so the share card's bars always agree with what the page itself shows.
function clampPct(p: number): number { return Math.max(6, Math.min(100, p)) }
function koPct(r: any): number | null {
  if (!r?.wins || r?.winsKo == null) return null
  return Math.round((r.winsKo / r.wins) * 100)
}
const STAT_METRICS = [
  { label: 'Reach', get: (s: any) => s?.physical?.reachInches ?? null, min: 60, max: 90, disp: (v: number) => `${v}"` },
  { label: 'Height', get: (s: any) => s?.physical?.heightInches ?? null, min: 60, max: 84, disp: (v: number) => `${Math.floor(v / 12)}'${v % 12}"` },
  { label: 'KO ratio', get: (s: any) => koPct(s?.record), min: 0, max: 100, disp: (v: number) => `${v}%` },
]

export interface CardStatRow {
  label: string
  redDisp: string; blueDisp: string
  redPct: number; bluePct: number
  redEdge: boolean; blueEdge: boolean
}

function statRows(a: any, b: any): CardStatRow[] {
  const out: CardStatRow[] = []
  for (const m of STAT_METRICS) {
    const rv = m.get(a), bv = m.get(b)
    if (rv == null || bv == null) continue
    out.push({
      label: m.label,
      redDisp: m.disp(rv), blueDisp: m.disp(bv),
      redPct: clampPct(((rv - m.min) / (m.max - m.min)) * 100),
      bluePct: clampPct(((bv - m.min) / (m.max - m.min)) * 100),
      redEdge: rv > bv, blueEdge: bv > rv,
    })
  }
  return out
}

// Props for the OgImage/FightCard share cards, built from a /api/fights/{slug}
// payload. Shared by the bout hub and article pages (auto-imported by Nuxt).
// `stats` is JSON-stringified: og-image's static prerendering encodes props
// into the build, and a plain string prop is the safest shape to carry
// through that unscathed (vs. a nested array/object prop).
export function fightCardProps(f: any) {
  const rec = (s: any): string | undefined => {
    const r = s?.record ?? {}
    if (r.wins == null) return undefined
    return `${r.wins}-${r.losses ?? 0}-${r.draws ?? 0}`
  }
  const res = f?.bout?.result
  const resultLine = res?.winner
    ? `${res.winner} wins${res.method ? ` by ${res.method}` : ''}${res.round ? `, round ${res.round}` : ''}`
    : undefined
  const rows = statRows(f?.fighterA, f?.fighterB)
  const ageA = f?.fighterA?.physical?.age, ageB = f?.fighterB?.physical?.age
  return {
    fighterA: f?.bout?.fighter_a,
    fighterB: f?.bout?.fighter_b,
    recordA: rec(f?.fighterA),
    recordB: rec(f?.fighterB),
    division: f?.bout?.weightClass ?? undefined,
    date: formatEventDate(f?.bout?.eventDate) ?? undefined,
    status: f?.bout?.status ?? undefined,
    resultLine,
    stats: rows.length ? JSON.stringify(rows) : undefined,
    ageA: ageA ?? undefined,
    ageB: ageB ?? undefined,
  }
}
