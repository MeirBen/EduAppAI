import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { dismissibleTooltips } from './shared/dismissible-tooltips';

/** Root outlet keeps parent and child identities and shells separate. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  constructor() {
    dismissibleTooltips();
  }
}
