export const imageWidth = 1920
export const imageHeight = 1280

export type FieldBoundary = { id: string; points: [number, number][] }

/**
 * Boundaries traced from visible field edges in field-atlas.webp (1920×1280 image coordinates), a photograph of
 * farmland in Biei, Hokkaido. Re-trace them whenever the image is replaced or re-cropped.
 */
export const fieldBoundaries: FieldBoundary[] = [
  { id: 'golden-barley', points: [[1200, 1088], [1353, 1087], [1520, 1155], [1538, 1170], [1450, 1178], [1310, 1227], [1240, 1157]] },
  { id: 'pale-stubble', points: [[1093, 1040], [1210, 1028], [1265, 1073], [1187, 1082], [1122, 1080]] },
  { id: 'lone-tree', points: [[1232, 1023], [1270, 1018], [1353, 1063], [1350, 1080], [1273, 1078]] },
  { id: 'long-strip', points: [[1288, 1008], [1345, 987], [1517, 1108], [1490, 1107], [1360, 1058]] },
  { id: 'green-lea', points: [[960, 1182], [1170, 1169], [1237, 1247], [1040, 1258], [997, 1217]] },
  { id: 'shaded-fallow', points: [[1463, 531], [1573, 519], [1670, 560], [1733, 598], [1640, 599], [1520, 577]] },
  { id: 'hillside-wheat', points: [[1370, 743], [1517, 739], [1568, 776], [1400, 800], [1375, 780]] },
  { id: 'bright-oats', points: [[967, 802], [1057, 772], [1142, 829], [1032, 868], [1000, 837]] },
  { id: 'ridge-plot', points: [[1185, 481], [1253, 466], [1310, 498], [1247, 517]] },
]

/** Display name derived from a boundary id, e.g. 'golden-barley' → 'Golden barley'. */
export const fieldLabel = (id: string) => id.charAt(0).toUpperCase() + id.slice(1).replaceAll('-', ' ')
