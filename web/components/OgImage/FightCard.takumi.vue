<script setup lang="ts">
// 1200x630 share card, rendered to PNG at build time by nuxt-og-image
// (satori). Satori supports a small CSS subset: flexbox + inline styles only,
// no CSS variables, no grid — hence the hardcoded Fight Bill palette values.
// With no fighter props it renders the default branded card (home/schedule).
const props = withDefaults(defineProps<{
  fighterA?: string
  fighterB?: string
  recordA?: string
  recordB?: string
  division?: string
  date?: string
  status?: string
  resultLine?: string
  stats?: string // JSON-encoded CardStatRow[] — see utils/ogCard.ts
  ageA?: number
  ageB?: number
}>(), {})

const isFight = computed(() => !!(props.fighterA && props.fighterB))
const statRows = computed<any[]>(() => {
  if (!props.stats) return []
  try { return JSON.parse(props.stats) } catch { return [] }
})
const hasAges = computed(() => props.ageA != null && props.ageB != null)

// Anton at 84px fits ~14 characters on a half-width column; step down for
// long names ("Richard Riakporhe") so nothing clips.
function nameSize(name?: string): string {
  const len = name?.length ?? 0
  if (len > 18) return '54px'
  if (len > 13) return '66px'
  return '84px'
}

const footParts = computed(() => {
  const parts: string[] = []
  if (props.division) parts.push(props.division.toUpperCase())
  if (props.date) parts.push(props.date.toUpperCase())
  if (props.status && !props.resultLine) parts.push(props.status.toUpperCase())
  return parts
})
</script>

<template>
  <div :style="{
    width: '1200px', height: '630px', display: 'flex', flexDirection: 'column',
    backgroundColor: '#0a0a0c', color: '#f4efe6', padding: '26px',
  }">
    <!-- fight-bill frame -->
    <div :style="{
      display: 'flex', flexDirection: 'column', flexGrow: 1,
      border: '2px solid #34313b', padding: '10px',
    }">
      <div :style="{
        display: 'flex', flexDirection: 'column', flexGrow: 1, alignItems: 'center',
        justifyContent: 'space-between', border: '1px solid #26242a',
        padding: '36px 48px 28px', backgroundColor: '#101015',
      }">
        <!-- wordmark -->
        <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center' }">
          <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '40px', letterSpacing: '6px' }">
            <span :style="{ color: '#f4efe6' }">BRIT</span>
            <span :style="{ color: '#e8b84b' }">BOXING</span>
          </div>
          <div v-if="!isFight" :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 500, fontSize: '20px',
            letterSpacing: '5px', color: '#9a958c', marginTop: '10px',
          }">THE BIG FIGHTS, PREVIEWED</div>
        </div>

        <!-- the matchup -->
        <div v-if="isFight" :style="{ display: 'flex', alignItems: 'center', justifyContent: 'center', width: '100%', gap: '36px' }">
          <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '460px' }">
            <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '20px', letterSpacing: '4px', color: '#e23350' }">RED CORNER</div>
            <div :style="{
              display: 'flex', fontFamily: 'Anton', fontSize: nameSize(fighterA), lineHeight: 1.05,
              textTransform: 'uppercase', textAlign: 'center', marginTop: '10px',
            }">{{ fighterA }}</div>
            <div v-if="recordA" :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '26px', letterSpacing: '3px', color: '#e8b84b', marginTop: '12px' }">{{ recordA }}</div>
          </div>

          <div :style="{
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            width: '92px', height: '92px', borderRadius: '46px', border: '3px solid #34313b',
            fontFamily: 'Anton', fontSize: '34px', color: '#f4efe6', backgroundColor: '#0c0c10',
          }">VS</div>

          <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '460px' }">
            <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '20px', letterSpacing: '4px', color: '#5b8be0' }">BLUE CORNER</div>
            <div :style="{
              display: 'flex', fontFamily: 'Anton', fontSize: nameSize(fighterB), lineHeight: 1.05,
              textTransform: 'uppercase', textAlign: 'center', marginTop: '10px',
            }">{{ fighterB }}</div>
            <div v-if="recordB" :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '26px', letterSpacing: '3px', color: '#e8b84b', marginTop: '12px' }">{{ recordB }}</div>
          </div>
        </div>

        <!-- tale of the tape: opposing bars from a shared centre, same metrics
             and scale as the on-page version (utils/ogCard.ts statRows) -->
        <div v-if="statRows.length || hasAges" :style="{
          display: 'flex', flexDirection: 'column', width: '100%', gap: '10px', alignItems: 'center',
        }">
          <div :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '15px',
            letterSpacing: '4px', color: '#9a958c',
          }">TALE OF THE TAPE</div>

          <div v-for="row in statRows" :key="row.label" :style="{ display: 'flex', flexDirection: 'row', alignItems: 'center', width: '1040px', gap: '14px' }">
            <div :style="{ display: 'flex', flexDirection: 'row', alignItems: 'center', justifyContent: 'flex-end', width: '460px', gap: '12px' }">
              <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '20px', color: row.redEdge ? '#e8b84b' : '#f4efe6' }">{{ row.redDisp }}</div>
              <div :style="{ display: 'flex', width: '260px', height: '14px', borderRadius: '7px', backgroundColor: '#1b1a20', justifyContent: 'flex-end', overflow: 'hidden' }">
                <div :style="{ display: 'flex', width: row.redPct + '%', height: '100%', borderRadius: '7px', backgroundColor: '#e23350' }" />
              </div>
            </div>
            <div :style="{ display: 'flex', width: '120px', justifyContent: 'center', fontFamily: 'Oswald', fontWeight: 500, fontSize: '14px', letterSpacing: '2px', color: '#9a958c' }">{{ row.label.toUpperCase() }}</div>
            <div :style="{ display: 'flex', flexDirection: 'row', alignItems: 'center', width: '460px', gap: '12px' }">
              <div :style="{ display: 'flex', width: '260px', height: '14px', borderRadius: '7px', backgroundColor: '#1b1a20', overflow: 'hidden' }">
                <div :style="{ display: 'flex', width: row.bluePct + '%', height: '100%', borderRadius: '7px', backgroundColor: '#5b8be0' }" />
              </div>
              <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '20px', color: row.blueEdge ? '#e8b84b' : '#f4efe6' }">{{ row.blueDisp }}</div>
            </div>
          </div>

          <div v-if="hasAges" :style="{ display: 'flex', flexDirection: 'row', alignItems: 'center', width: '1040px', gap: '14px' }">
            <div :style="{ display: 'flex', width: '460px', justifyContent: 'flex-end', fontFamily: 'Oswald', fontWeight: 600, fontSize: '20px', color: '#f4efe6' }">{{ ageA }}</div>
            <div :style="{ display: 'flex', width: '120px', justifyContent: 'center', fontFamily: 'Oswald', fontWeight: 500, fontSize: '14px', letterSpacing: '2px', color: '#9a958c' }">AGE</div>
            <div :style="{ display: 'flex', width: '460px', fontFamily: 'Oswald', fontWeight: 600, fontSize: '20px', color: '#f4efe6' }">{{ ageB }}</div>
          </div>
        </div>

        <!-- default card centrepiece -->
        <div v-else :style="{
          display: 'flex', fontFamily: 'Anton', fontSize: '92px', textTransform: 'uppercase',
          textAlign: 'center', lineHeight: 1.1,
        }">UK BOXING NEWS &amp; FIGHT PREVIEWS</div>

        <!-- foot strip -->
        <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center' }">
          <div v-if="resultLine" :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '28px',
            letterSpacing: '4px', color: '#e8b84b', marginBottom: '10px', textTransform: 'uppercase',
          }">{{ resultLine }}</div>
          <div v-if="footParts.length" :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 500, fontSize: '22px',
            letterSpacing: '4px', color: '#9a958c',
          }">{{ footParts.join('  ·  ') }}</div>
          <div :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 500, fontSize: '18px',
            letterSpacing: '3px', color: '#6a6a72', marginTop: '10px',
          }">BRITBOXING.CO.UK</div>
        </div>
      </div>
    </div>
  </div>
</template>
