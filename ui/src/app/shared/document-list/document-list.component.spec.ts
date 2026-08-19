import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DocumentListComponent, DocRow } from './document-list.component';

describe('DocumentListComponent', () => {
  let fixture: ComponentFixture<DocumentListComponent>;
  let component: DocumentListComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [DocumentListComponent] }).compileComponents();
    fixture = TestBed.createComponent(DocumentListComponent);
    component = fixture.componentInstance;
  });

  const row = (key: string, batchId: string | null): DocRow => ({
    key, ownerKind: 'land', ownerId: 'land-1', documentId: key, fileName: `${key}.pdf`,
    contentType: 'application/pdf', uploadedByName: 'A', createdAt: '2026-01-01', batchId
  });

  it('renders a batch of one as a plain row, no group chrome', () => {
    component.rows = [row('a', 'batch-1')];
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).not.toContain('files');
    expect(el.querySelectorAll('[data-testid="group-header"]').length).toBe(0);
  });

  it('groups 2+ rows sharing a batchId under one collapsible header', () => {
    component.rows = [row('a', 'batch-1'), row('b', 'batch-1'), row('c', null)];
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelectorAll('[data-testid="group-header"]').length).toBe(1);
    expect(el.textContent).toContain('2 files');
  });

  it('emits removeGroup with every member row', () => {
    component.rows = [row('a', 'batch-1'), row('b', 'batch-1')];
    fixture.detectChanges();
    let emitted: DocRow[] | undefined;
    component.removeGroup.subscribe((rows: DocRow[]) => (emitted = rows));
    component.confirmRemoveGroup('batch-1');
    expect(emitted?.map(r => r.key).sort()).toEqual(['a', 'b']);
  });
});
