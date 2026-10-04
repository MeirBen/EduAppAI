import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { Theme, ThemePreference } from '../../core/theme/theme';

interface ThemeOption {
  readonly value: ThemePreference;
  readonly label: string;
  /** Icon class from the shared icon set. */
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
    { value: 'system', label: 'לפי המכשיר', icon: 'icon-monitor' },
    { value: 'light', label: 'בהיר', icon: 'icon-sun' },
    { value: 'dark', label: 'כהה', icon: 'icon-moon' },
  ];
}
