// Props for the OgImage/FightCard share card, built from a /api/fights/{slug}
// payload. Shared by the bout hub and article pages (auto-imported by Nuxt).
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
  return {
    fighterA: f?.bout?.fighter_a,
    fighterB: f?.bout?.fighter_b,
    recordA: rec(f?.fighterA),
    recordB: rec(f?.fighterB),
    division: f?.bout?.weightClass ?? undefined,
    date: formatEventDate(f?.bout?.eventDate) ?? undefined,
    status: f?.bout?.status ?? undefined,
    resultLine,
  }
}
