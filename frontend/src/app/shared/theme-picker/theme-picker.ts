import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Theme, ThemePreference } from '../../core/theme/theme';

interface ThemeOption {
  readonly value: ThemePreference;
  readonly label: string;
  /** 24×24 stroked icon path. */
  readonly icon: string;
}

/** Native radio group for the device color theme; arrow keys move between options. */
@Component({
  selector: 'app-theme-picker',
  templateUrl: './theme-picker.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ThemePicker {
  protected readonly theme = inject(Theme);
  protected readonly options: readonly ThemeOption[] = [
    { value: 'system', label: 'לפי המכשיר', icon: 'M4 5h16v11H4zM9 20h6m-3-4v4' },
    {
      value: 'light',
      label: 'בהיר',
      icon: 'M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8Zm0-5v2m0 14v2M3 12h2m14 0h2M5.6 5.6 7 7m10 10 1.4 1.4M5.6 18.4 7 17M17 7l1.4-1.4',
    },
    { value: 'dark', label: 'כהה', icon: 'M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5Z' },
  ];
}
