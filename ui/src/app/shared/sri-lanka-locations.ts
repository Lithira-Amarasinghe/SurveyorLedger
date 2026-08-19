/** Sri Lanka's 9 provinces and 25 districts - used by every province/district <select> pair (Land's address form, add-land-widget's quick-create). */
export const DISTRICTS_BY_PROVINCE: Record<string, string[]> = {
  'Western Province': ['Colombo', 'Gampaha', 'Kalutara'],
  'Central Province': ['Kandy', 'Matale', 'Nuwara Eliya'],
  'Southern Province': ['Galle', 'Matara', 'Hambantota'],
  'Northern Province': ['Jaffna', 'Kilinochchi', 'Mannar', 'Vavuniya', 'Mullaitivu'],
  'Eastern Province': ['Trincomalee', 'Batticaloa', 'Ampara'],
  'North Western Province': ['Kurunegala', 'Puttalam'],
  'North Central Province': ['Anuradhapura', 'Polonnaruwa'],
  'Uva Province': ['Badulla', 'Monaragala'],
  'Sabaragamuwa Province': ['Ratnapura', 'Kegalle']
};

export const PROVINCES: string[] = Object.keys(DISTRICTS_BY_PROVINCE);

const DISTRICT_TO_PROVINCE: Record<string, string> = Object.fromEntries(
  Object.entries(DISTRICTS_BY_PROVINCE).flatMap(([province, districts]) => districts.map(d => [d, province]))
);

export function provinceForDistrict(district: string): string | undefined {
  return DISTRICT_TO_PROVINCE[district];
}

/** Flattened, alphabetical - for a lone district select with no paired province field (add-land-widget). */
export const ALL_DISTRICTS: string[] = Object.values(DISTRICTS_BY_PROVINCE).flat().sort();
