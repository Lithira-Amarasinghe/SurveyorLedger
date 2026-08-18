/** RFC 4180 field quoting - wraps in quotes and doubles internal quotes whenever the
 * field contains a comma, quote, or newline. Plain fields pass through unquoted. */
function escapeCsvField(value: unknown): string {
  const str = value === null || value === undefined ? '' : String(value);
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
