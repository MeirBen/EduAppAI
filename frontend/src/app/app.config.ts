import {
  ApplicationConfig,
  isDevMode,
  LOCALE_ID,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { DATE_PIPE_DEFAULT_OPTIONS, registerLocaleData } from '@angular/common';
import hebrew from '@angular/common/locales/he';
import { provideHttpClient } from '@angular/common/http';
import {
  provideRouter,
  RouteReuseStrategy,
  withComponentInputBinding,
  withInMemoryScrolling,
} from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { routes } from './app.routes';
import { PageReuseStrategy } from './core/page-reuse-strategy';

registerLocaleData(hebrew);

/** Shared native providers for the parent application. */
export const appConfig: ApplicationConfig = {
  providers: [
    { provide: LOCALE_ID, useValue: 'he-IL' },
    // One date style everywhere: a month name avoids day/month ambiguity, and the time is local.
    { provide: DATE_PIPE_DEFAULT_OPTIONS, useValue: { dateFormat: 'd בMMM y, H:mm' } },
    provideBrowserGlobalErrorListeners(),
    // Default XSRF names match AuthConfiguration and the readable token issued by AuthEndpoints.
    provideHttpClient(),
    // New pages open at the top; Back restores the position the parent left.
    provideRouter(
      routes,
      withComponentInputBinding(),
      withInMemoryScrolling({ scrollPositionRestoration: 'enabled' }),
    ),
    { provide: RouteReuseStrategy, useClass: PageReuseStrategy },
    provideServiceWorker('ngsw-worker.js', {
      enabled: !isDevMode(),
      registrationStrategy: 'registerWhenStable:30000',
    }),
  ],
};
