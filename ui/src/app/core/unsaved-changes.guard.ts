import { CanDeactivateFn } from '@angular/router';
import { Observable } from 'rxjs';

/** Implemented by any page that holds edits worth confirming before it's navigated away from. */
export interface HasUnsavedChanges {
  canDeactivate(): boolean | Observable<boolean>;
}

/**
 * Blocks in-app navigation while a page reports unsaved edits, letting the page itself
 * decide how to ask (so it can offer Save/Discard rather than a bare OK/Cancel).
 * Browser refresh and tab close can't be intercepted this way - pages pair this with a
 * `beforeunload` listener, where only the browser's own dialog is permitted.
 */
export const unsavedChangesGuard: CanDeactivateFn<HasUnsavedChanges> = (component) =>
  component?.canDeactivate ? component.canDeactivate() : true;
