export const imageWidth = 1920
export const imageHeight = 1280

export type FieldBoundary = { id: string; points: [number, number][] }

/** Boundaries traced from visible field edges in field-atlas.webp (1920×1280 image coordinates). */
export const fieldBoundaries: FieldBoundary[] = [
  { id: 'north-stubble', points: [[1123, 210], [1344, 112], [1394, 105], [1482, 126], [1392, 242], [1328, 281], [1203, 312], [1165, 284]] },
  { id: 'river-rows', points: [[1258, 377], [1340, 343], [1388, 366], [1491, 420], [1378, 553], [1323, 520], [1285, 496], [1264, 463]] },
  { id: 'east-rows', points: [[1634, 242], [1786, 264], [1845, 276], [1770, 383], [1635, 436], [1550, 402]] },
  { id: 'meadow', points: [[1504, 425], [1622, 484], [1504, 686], [1476, 667], [1442, 614], [1392, 560]] },
  { id: 'long-rows', points: [[1637, 524], [1790, 578], [1667, 864], [1519, 692]] },
  { id: 'centre-green', points: [[1110, 800], [1300, 792], [1466, 812], [1478, 1040], [1430, 1080], [1095, 893], [1098, 850]] },
  { id: 'south-stubble', points: [[1093, 908], [1428, 1096], [1360, 1153], [1278, 1252], [1150, 1256], [942, 1045], [1000, 962]] },
]

/** Display name derived from a boundary id, e.g. 'north-stubble' → 'North stubble'. */
export const fieldLabel = (id: string) => id.charAt(0).toUpperCase() + id.slice(1).replaceAll('-', ' ')
