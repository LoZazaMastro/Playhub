export const STORE_LAYOUT_PROFILES = {
  narrow: {
    maximumWidth: 700,
    headerColumns: "minmax(0,1fr) auto",
    gridMinimum: 190,
    listImageWidth: 132,
  },
  normal: {
    minimumWidth: 701,
    headerColumns: "minmax(0,1fr) auto",
    gridMinimum: 260,
    listImageWidth: 210,
  },
} as const;

export function storeLayoutForWidth(width: number) {
  return width <= STORE_LAYOUT_PROFILES.narrow.maximumWidth
    ? STORE_LAYOUT_PROFILES.narrow
    : STORE_LAYOUT_PROFILES.normal;
}
