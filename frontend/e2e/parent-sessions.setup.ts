import { expect, test as setup } from '@playwright/test';
import { sessionFile } from './parent-session';
import parents from './parents.json';

// The server allows ten sign-ins a minute per address, so each parent signs in once and the workflows reuse it.
for (const email of parents.emails)
  setup(`sign in ${email}`, async ({ request }) => {
    const { token } = await (await request.get('/api/auth/csrf')).json();
    const login = await request.post('/api/auth/login', {
      headers: { 'X-XSRF-TOKEN': token },
      data: { email, password: parents.password },
    });
    expect(login.status()).toBe(204);
    await request.storageState({ path: sessionFile(email) });
  });
