import { PROVINCES, DISTRICTS_BY_PROVINCE, provinceForDistrict } from './sri-lanka-locations';

describe('sri-lanka-locations', () => {
  it('has 9 provinces', () => expect(PROVINCES.length).toBe(9));

  it('has 25 districts total across all provinces', () => {
    const total = Object.values(DISTRICTS_BY_PROVINCE).reduce((sum, list) => sum + list.length, 0);
    expect(total).toBe(25);
  });

  it('every province in PROVINCES has a district list', () => {
    for (const province of PROVINCES) expect(DISTRICTS_BY_PROVINCE[province]?.length).toBeGreaterThan(0);
  });

  it('provinceForDistrict finds the right province', () => {
    expect(provinceForDistrict('Colombo')).toBe('Western Province');
    expect(provinceForDistrict('Kandy')).toBe('Central Province');
  });

  it('provinceForDistrict returns undefined for an unknown district', () => {
    expect(provinceForDistrict('Nowhere')).toBeUndefined();
  });
});
