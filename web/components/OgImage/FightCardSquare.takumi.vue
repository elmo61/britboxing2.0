<script setup lang="ts">
// 1080x1080 square variant of FightCard.takumi.vue — same content, narrower
// columns to fit the tighter width relative to height. See that file for the
// satori/takumi rendering notes (flexbox + inline styles only).
const props = withDefaults(defineProps<{
  fighterA?: string
  fighterB?: string
  recordA?: string
  recordB?: string
  division?: string
  date?: string
  status?: string
  resultLine?: string
  stats?: string
  ageA?: number
  ageB?: number
}>(), {})

const isFight = computed(() => !!(props.fighterA && props.fighterB))
const statRows = computed<any[]>(() => {
  if (!props.stats) return []
  try { return JSON.parse(props.stats) } catch { return [] }
})
const hasAges = computed(() => props.ageA != null && props.ageB != null)

function nameSize(name?: string): string {
  const len = name?.length ?? 0
  if (len > 18) return '46px'
  if (len > 13) return '56px'
  return '68px'
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
    width: '1080px', height: '1080px', display: 'flex', flexDirection: 'column',
    backgroundColor: '#0a0a0c', color: '#f4efe6', padding: '30px',
  }">
    <div :style="{
      display: 'flex', flexDirection: 'column', flexGrow: 1,
      border: '2px solid #34313b', padding: '12px',
    }">
      <div :style="{
        display: 'flex', flexDirection: 'column', flexGrow: 1, alignItems: 'center',
        justifyContent: 'center', border: '1px solid #26242a', gap: '46px',
        padding: '40px 40px 34px', backgroundColor: '#101015',
      }">
        <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center' }">
          <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '38px', letterSpacing: '6px' }">
            <span :style="{ color: '#f4efe6' }">BRIT</span>
            <span :style="{ color: '#e8b84b' }">BOXING</span>
          </div>
          <div v-if="!isFight" :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 500, fontSize: '20px',
            letterSpacing: '5px', color: '#9a958c', marginTop: '10px',
          }">THE BIG FIGHTS, PREVIEWED</div>
        </div>

        <div v-if="isFight" :style="{ display: 'flex', alignItems: 'center', justifyContent: 'center', width: '100%', gap: '24px' }">
          <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '390px' }">
            <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '18px', letterSpacing: '3px', color: '#e23350' }">RED CORNER</div>
            <div :style="{
              display: 'flex', fontFamily: 'Anton', fontSize: nameSize(fighterA), lineHeight: 1.08,
              textTransform: 'uppercase', textAlign: 'center', marginTop: '10px',
            }">{{ fighterA }}</div>
            <div v-if="recordA" :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '24px', letterSpacing: '2px', color: '#e8b84b', marginTop: '12px' }">{{ recordA }}</div>
          </div>

          <div :style="{
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            width: '76px', height: '76px', borderRadius: '38px', border: '3px solid #34313b',
            fontFamily: 'Anton', fontSize: '28px', color: '#f4efe6', backgroundColor: '#0c0c10',
          }">VS</div>

          <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '390px' }">
            <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '18px', letterSpacing: '3px', color: '#5b8be0' }">BLUE CORNER</div>
            <div :style="{
              display: 'flex', fontFamily: 'Anton', fontSize: nameSize(fighterB), lineHeight: 1.08,
              textTransform: 'uppercase', textAlign: 'center', marginTop: '10px',
            }">{{ fighterB }}</div>
            <div v-if="recordB" :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '24px', letterSpacing: '2px', color: '#e8b84b', marginTop: '12px' }">{{ recordB }}</div>
          </div>
        </div>

        <div v-else :style="{
          display: 'flex', fontFamily: 'Anton', fontSize: '76px', textTransform: 'uppercase',
          textAlign: 'center', lineHeight: 1.15,
        }">UK BOXING NEWS &amp; FIGHT PREVIEWS</div>

        <div v-if="statRows.length || hasAges" :style="{
          display: 'flex', flexDirection: 'column', width: '100%', gap: '18px', alignItems: 'center',
        }">
          <div :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '17px',
            letterSpacing: '4px', color: '#9a958c',
          }">TALE OF THE TAPE</div>

          <div v-for="row in statRows" :key="row.label" :style="{ display: 'flex', flexDirection: 'row', alignItems: 'center', width: '860px', gap: '14px' }">
            <div :style="{ display: 'flex', flexDirection: 'row', alignItems: 'center', justifyContent: 'flex-end', width: '370px', gap: '12px' }">
              <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '22px', color: row.redEdge ? '#e8b84b' : '#f4efe6' }">{{ row.redDisp }}</div>
              <div :style="{ display: 'flex', width: '210px', height: '16px', borderRadius: '8px', backgroundColor: '#1b1a20', justifyContent: 'flex-end', overflow: 'hidden' }">
                <div :style="{ display: 'flex', width: row.redPct + '%', height: '100%', borderRadius: '8px', backgroundColor: '#e23350' }" />
              </div>
            </div>
            <div :style="{ display: 'flex', width: '120px', justifyContent: 'center', fontFamily: 'Oswald', fontWeight: 500, fontSize: '15px', letterSpacing: '2px', color: '#9a958c' }">{{ row.label.toUpperCase() }}</div>
            <div :style="{ display: 'flex', flexDirection: 'row', alignItems: 'center', width: '370px', gap: '12px' }">
              <div :style="{ display: 'flex', width: '210px', height: '16px', borderRadius: '8px', backgroundColor: '#1b1a20', overflow: 'hidden' }">
                <div :style="{ display: 'flex', width: row.bluePct + '%', height: '100%', borderRadius: '8px', backgroundColor: '#5b8be0' }" />
              </div>
              <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '22px', color: row.blueEdge ? '#e8b84b' : '#f4efe6' }">{{ row.blueDisp }}</div>
            </div>
          </div>

          <div v-if="hasAges" :style="{ display: 'flex', flexDirection: 'row', alignItems: 'center', width: '860px', gap: '14px' }">
            <div :style="{ display: 'flex', width: '370px', justifyContent: 'flex-end', fontFamily: 'Oswald', fontWeight: 600, fontSize: '22px', color: '#f4efe6' }">{{ ageA }}</div>
            <div :style="{ display: 'flex', width: '120px', justifyContent: 'center', fontFamily: 'Oswald', fontWeight: 500, fontSize: '15px', letterSpacing: '2px', color: '#9a958c' }">AGE</div>
            <div :style="{ display: 'flex', width: '370px', fontFamily: 'Oswald', fontWeight: 600, fontSize: '22px', color: '#f4efe6' }">{{ ageB }}</div>
          </div>
        </div>

        <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center' }">
          <div v-if="resultLine" :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '26px',
            letterSpacing: '3px', color: '#e8b84b', marginBottom: '10px', textTransform: 'uppercase', textAlign: 'center',
          }">{{ resultLine }}</div>
          <div v-if="footParts.length" :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 500, fontSize: '20px',
            letterSpacing: '3px', color: '#9a958c',
          }">{{ footParts.join('  ·  ') }}</div>
          <div :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 500, fontSize: '17px',
            letterSpacing: '3px', color: '#6a6a72', marginTop: '10px',
          }">BRITBOXING.CO.UK</div>
        </div>
      </div>
    </div>
  </div>
</template>
