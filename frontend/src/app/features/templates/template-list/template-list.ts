import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  inject,
  resource,
  signal,
  viewChild,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../../core/api/learning-api';
import { apiError } from '../../../core/api/api-error';

type Removal = { kind: 'template' | 'instance' | 'library'; id: string; name: string };

/** Family library with explicit confirmation before permanent content deletion. */
@Component({
  selector: 'app-template-list',
  imports: [RouterLink, DatePipe],
  templateUrl: './template-list.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TemplateList {
  private readonly api = inject(LearningApi);
  protected readonly confirmation =
    viewChild.required<ElementRef<HTMLDialogElement>>('confirmation');
  private readonly heading = viewChild.required<ElementRef<HTMLHeadingElement>>('heading');
  protected readonly removal = signal<Removal | null>(null);
  protected readonly deleting = signal(false);
  protected readonly deletionError = signal('');
  protected readonly notice = signal('');
  protected readonly data = resource({
    loader: async () => {
      const [templates, instances] = await Promise.all([
        this.api.listTemplates(),
        this.api.listInstances(),
      ]);
      return { templates, instances };
    },
  });
  protected readonly apiError = apiError;

  protected requestRemoval(kind: Removal['kind'], id = '', name = '') {
    this.removal.set({ kind, id, name });
    this.deletionError.set('');
    this.notice.set('');
    this.confirmation().nativeElement.showModal();
  }

  protected onCancel(event: Event) {
    if (this.deleting()) event.preventDefault();
  }

  protected async remove() {
    const target = this.removal();
    if (!target || this.deleting()) return;
    this.deleting.set(true);
    this.deletionError.set('');
    try {
      switch (target.kind) {
        case 'template':
          await this.api.deleteTemplate(target.id);
          this.notice.set('התבנית והטיוטות שלה נמחקו.');
          break;
        case 'instance':
          await this.api.deleteInstance(target.id);
          this.notice.set('הטיוטה נמחקה.');
          break;
        case 'library':
          await this.api.resetLibrary();
          this.notice.set('נתוני הלמידה אופסו. אפשר להתחיל עם רעיון חדש.');
          break;
      }
      this.confirmation().nativeElement.close();
      this.data.reload();
      this.heading().nativeElement.focus();
    } catch (error) {
      this.deletionError.set(apiError(error));
    } finally {
      this.deleting.set(false);
    }
  }
}
