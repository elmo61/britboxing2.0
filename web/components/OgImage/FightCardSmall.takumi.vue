<script setup lang="ts">
// 600x315 compact variant — exactly half-scale of FightCard.takumi.vue's
// 1200x630, minus the stat bars (illegible at this size). For lightweight
// use: article-list thumbnails, messaging previews, anywhere a smaller file
// is worth more than the tale-of-the-tape detail.
const props = withDefaults(defineProps<{
  fighterA?: string
  fighterB?: string
  recordA?: string
  recordB?: string
  division?: string
  date?: string
  status?: string
  resultLine?: string
}>(), {})

const isFight = computed(() => !!(props.fighterA && props.fighterB))

function nameSize(name?: string): string {
  const len = name?.length ?? 0
  if (len > 18) return '27px'
  if (len > 13) return '33px'
  return '42px'
}

const footParts = computed(() => {
  const parts: string[] = []
  if (props.division) parts.push(props.division.toUpperCase())
  if (props.status && !props.resultLine) parts.push(props.status.toUpperCase())
  return parts
})
</script>

<template>
  <div :style="{
    width: '600px', height: '315px', display: 'flex', flexDirection: 'column',
    backgroundColor: '#0a0a0c', color: '#f4efe6', padding: '13px',
  }">
    <div :style="{
      display: 'flex', flexDirection: 'column', flexGrow: 1,
      border: '1px solid #34313b', padding: '5px',
    }">
      <div :style="{
        display: 'flex', flexDirection: 'column', flexGrow: 1, alignItems: 'center',
        justifyContent: 'space-between', border: '1px solid #26242a',
        padding: '18px 24px 14px', backgroundColor: '#101015',
      }">
        <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '20px', letterSpacing: '3px' }">
          <span :style="{ color: '#f4efe6' }">BRIT</span>
          <span :style="{ color: '#e8b84b' }">BOXING</span>
        </div>

        <div v-if="isFight" :style="{ display: 'flex', alignItems: 'center', justifyContent: 'center', width: '100%', gap: '18px' }">
          <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '230px' }">
            <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '10px', letterSpacing: '2px', color: '#e23350' }">RED CORNER</div>
            <div :style="{
              display: 'flex', fontFamily: 'Anton', fontSize: nameSize(fighterA), lineHeight: 1.05,
              textTransform: 'uppercase', textAlign: 'center', marginTop: '5px',
            }">{{ fighterA }}</div>
            <div v-if="recordA" :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '13px', letterSpacing: '1px', color: '#e8b84b', marginTop: '6px' }">{{ recordA }}</div>
          </div>

          <div :style="{
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            width: '46px', height: '46px', borderRadius: '23px', border: '2px solid #34313b',
            fontFamily: 'Anton', fontSize: '17px', color: '#f4efe6', backgroundColor: '#0c0c10',
          }">VS</div>

          <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center', width: '230px' }">
            <div :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '10px', letterSpacing: '2px', color: '#5b8be0' }">BLUE CORNER</div>
            <div :style="{
              display: 'flex', fontFamily: 'Anton', fontSize: nameSize(fighterB), lineHeight: 1.05,
              textTransform: 'uppercase', textAlign: 'center', marginTop: '5px',
            }">{{ fighterB }}</div>
            <div v-if="recordB" :style="{ display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '13px', letterSpacing: '1px', color: '#e8b84b', marginTop: '6px' }">{{ recordB }}</div>
          </div>
        </div>

        <div v-else :style="{
          display: 'flex', fontFamily: 'Anton', fontSize: '46px', textTransform: 'uppercase',
          textAlign: 'center', lineHeight: 1.1,
        }">UK BOXING NEWS &amp; FIGHT PREVIEWS</div>

        <div :style="{ display: 'flex', flexDirection: 'column', alignItems: 'center' }">
          <div v-if="resultLine" :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 600, fontSize: '14px',
            letterSpacing: '2px', color: '#e8b84b', marginBottom: '5px', textTransform: 'uppercase', textAlign: 'center',
          }">{{ resultLine }}</div>
          <div v-if="footParts.length" :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 500, fontSize: '11px',
            letterSpacing: '2px', color: '#9a958c',
          }">{{ footParts.join('  ·  ') }}</div>
          <div :style="{
            display: 'flex', fontFamily: 'Oswald', fontWeight: 500, fontSize: '9px',
            letterSpacing: '1.5px', color: '#6a6a72', marginTop: '5px',
          }">BRITBOXING.CO.UK</div>
        </div>
      </div>
    </div>
  </div>
</template>
