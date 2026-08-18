/** RFC 4180 field quoting - wraps in quotes and doubles internal quotes whenever the
 * field contains a comma, quote, or newline. Plain fields pass through unquoted.
 *
 * Also guards against CSV/formula injection: a field starting with =, +, -, or @ is
 * interpreted as a formula by Excel/Sheets when the file is opened - user-controlled
 * data here (client names, descriptions) could otherwise execute arbitrary formulas on
 * whoever opens the export. Prefixing with a single quote neutralizes it while keeping
 * the value readable. */
function escapeCsvField(value: unknown): string {
  let str = value === null || value === undefined ? '' : String(value);
  if (/^[=+\-@]/.test(str)) {
    str = `'${str}`;
  }
  if (/[",\n\r]/.test(str)) {
    return `"${str.replace(/"/g, '""')}"`;
  }
  return str;
}

/** Builds a CSV string from column headers + row objects, then triggers a browser
 * download. No backend endpoint - the caller already has the data as JSON. */
export function downloadCsv<T extends object>(
  filename: string,
  columns: { key: keyof T; header: string }[],
  rows: T[]
): void {
  const lines = [
    columns.map(c => escapeCsvField(c.header)).join(','),
    ...rows.map(row => columns.map(c => escapeCsvField(row[c.key])).join(','))
  ];
  const blob = new Blob([lines.join('\r\n')], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}
