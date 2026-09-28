# Family Learning client

Run `npm ci` then `npm start`. The API must be running on localhost:5124;
see the [root README](../README.md) for parent setup and the one-command startup.

- `npm test -- --watch=false`: component tests.
- `npm run build`: production PWA build.
- `npm run format`: format TypeScript, templates and styles.
- `npm run e2e`: isolated browser test after `../scripts/publish.sh`.

Read [the architecture guide](../docs/architecture.md) for the template/instance boundary.
