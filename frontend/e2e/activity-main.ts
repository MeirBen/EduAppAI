import { bootstrapApplication } from '@angular/platform-browser';
import { App } from '../src/app/app';
import { applicationConfig } from '../src/app/app.config';
import { contentFirstRoutes } from '../src/app/app.routes';

// Native test bootstrap selects the approved staged routes without a production runtime flag.
bootstrapApplication(App, applicationConfig(contentFirstRoutes)).catch((error) =>
  console.error(error),
);
