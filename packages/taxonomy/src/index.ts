export const powertrains = ["ICE", "HEV", "PHEV", "EREV", "BEV"] as const;
export type Powertrain = (typeof powertrains)[number];

