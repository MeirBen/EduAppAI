import {
  ApplicationConfig,
  isDevMode,
  LOCALE_ID,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { registerLocaleData } from '@angular/common';
import hebrew from '@angular/common/locales/he';
import { provideHttpClient } from '@angular/common/http';
import {
  provideRouter,
  RouteReuseStrategy,
  Routes,
  withComponentInputBinding,
} from '@angular/router';
import { provideServiceWorker } from '@angular/service-worker';
import { routes } from './app.routes';
import { PageReuseStrategy } from './core/page-reuse-strategy';

registerLocaleData(hebrew);

/** One provider configuration with an explicit route composition selected by its bootstrap. */
export function applicationConfig(applicationRoutes: Routes): ApplicationConfig {
  return {
    providers: [
      { provide: LOCALE_ID, useValue: 'he-IL' },
      provideBrowserGlobalErrorListeners(),
      // Default XSRF names match AuthConfiguration and the readable token issued by AuthEndpoints.
      provideHttpClient(),
      provideRouter(applicationRoutes, withComponentInputBinding()),
      { provide: RouteReuseStrategy, useClass: PageReuseStrategy },
      provideServiceWorker('ngsw-worker.js', {
        enabled: !isDevMode(),
        registrationStrategy: 'registerWhenStable:30000',
      }),
    ],
  };
}

export const appConfig = applicationConfig(routes);
