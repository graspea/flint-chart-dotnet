import type { Ir } from '../../src/ir.ts';

/** A minimal IR: one chart type on two backends, no upstream types. Tests add what they need. */
export function syntheticIr(): Ir {
  return {
    upstream: { package: 'flint-chart', version: '0.5.1', integrity: 'sha512-test' },
    types: {},
    catalog: {
      backends: ['vegalite', 'excel'],
      backendsApplyingTheme: ['vegalite'],
      hostStateKeys: ['sort'],
      ignoredOptions: { maxStretchX: ['vegalite'] },
      channels: ['x', 'y', 'color'],
      chartTypes: [
        {
          name: 'Line Chart',
          backends: {
            vegalite: {
              category: 'Lines',
              channels: ['x', 'y', 'color'],
              properties: [
                {
                  key: 'interpolate',
                  label: 'Curve',
                  type: 'discrete',
                  options: [
                    { label: 'Default', omitted: true },
                    { label: 'Monotone', value: 'monotone' },
                  ],
                },
                { key: 'showPoints', label: 'Points', type: 'binary' },
                { key: 'strokeWidth', label: 'Width', type: 'continuous', min: 0.5, max: 5, step: 0.5 },
              ],
            },
            excel: {
              channels: ['x', 'y'],
              properties: [],
              typeMapping: { vertical: 'Line' },
              supportsFacets: false,
              hasValidate: true,
            },
          },
        },
      ],
      semanticTypes: [{ name: 'YearMonth', t0: 'Temporal', t1: 'DateGranule', visEncodings: ['temporal', 'ordinal'], doc: 'Year and month' }],
      themePresets: [{ id: 'powerbi-light', label: 'Power BI Light', description: 'Dashboards.' }],
    },
  };
}
