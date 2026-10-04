import { TestBed } from '@angular/core/testing';
import { CopyButton } from './copy-button';

describe('CopyButton', () => {
  afterEach(() => delete (navigator as { clipboard?: Clipboard }).clipboard);

  it('copies its text and announces the outcome, including a failure', async () => {
    const writeText = vi.fn().mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error());
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    const fixture = TestBed.createComponent(CopyButton);
    fixture.componentRef.setInput('text', 'תשובת השירות');
    fixture.componentRef.setInput('label', 'העתקת התשובה');
    const root: HTMLElement = fixture.nativeElement;
    const status = () => root.querySelector('[role="status"]')!.textContent!.trim();

    root.querySelector('button')!.click();
    await fixture.whenStable();
    expect(writeText).toHaveBeenCalledWith('תשובת השירות');
    expect(status()).toBe('הועתק');

    root.querySelector('button')!.click();
    await fixture.whenStable();
    expect(status()).toContain('סמנו את הטקסט והעתיקו ידנית');
  });
});
