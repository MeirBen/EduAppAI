import { of } from 'rxjs';
import { Limits } from './limits';
import data from './limits.fixture.json';
import { ContentLimits } from './models';

/** The server's current limits, shared with the isolated browser suites. */
export const limits: ContentLimits = data;

/** Supplies loaded limits without the guard's HTTP read. */
export const provideLimits = () => ({
  provide: Limits,
  useValue: { current: limits, load: () => of(limits) },
});
