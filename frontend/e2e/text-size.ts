import type { Page } from '@playwright/test';

/**
 * Sets the browser's default text size, as a parent's browser setting does. Unlike a page style,
 * media queries follow it, so layout that depends on text size behaves as it would for them.
 */
export async function textSize(page: Page, pixels: number) {
  const browser = await page.context().newCDPSession(page);
  await browser.send('Page.setFontSizes', { fontSizes: { standard: pixels } });
}
