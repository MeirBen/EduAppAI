import { Injectable } from '@angular/core';
import { ActivatedRouteSnapshot, BaseRouteReuseStrategy } from '@angular/router';

/** Keeps page state only for the same route and parameters; query/fragment changes preserve edits. */
@Injectable()
export class PageReuseStrategy extends BaseRouteReuseStrategy {
  override shouldReuseRoute(future: ActivatedRouteSnapshot, current: ActivatedRouteSnapshot) {
    // Destroying a previous entity's page also cancels its pending writes through DestroyRef.
    const keys = Object.keys(future.params);
    return (
      super.shouldReuseRoute(future, current) &&
      keys.length === Object.keys(current.params).length &&
      keys.every((key) => future.params[key] === current.params[key])
    );
  }
}
