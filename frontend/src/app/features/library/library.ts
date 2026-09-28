import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  ElementRef,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LearningApi } from '../../core/api/learning-api';
import { apiError } from '../../core/api/api-error';
import { LoadingIndicator } from '../../shared/loading-indicator/loading-indicator';

type Removal = { kind: 'template' | 'instance' | 'library'; id: string; name: string };

/** Family library with explicit confirmation before permanent content deletion. */
@Component({
  selector: 'app-library',
  imports: [LoadingIndicator, RouterLink, DatePipe],
  templateUrl: './library.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class Library {
  private readonly api = inject(LearningApi);
  private readonly lifetime = inject(DestroyRef);
  protected readonly confirmation =
    viewChild.required<ElementRef<HTMLDialogElement>>('confirmation');
  private readonly heading = viewChild.required<ElementRef<HTMLHeadingElement>>('heading');
  protected readonly removal = signal<Removal | null>(null);
  protected readonly deleting = signal(false);
  protected readonly deletionError = signal('');
  protected readonly notice = signal('');
  protected readonly templates = this.api.templates();
  protected readonly instances = this.api.instances();
  protected readonly loading = computed(
    () => this.templates.isLoading() || this.instances.isLoading(),
  );
  protected readonly loadError = computed(() => this.templates.error() ?? this.instances.error());
  protected readonly apiError = apiError;

  protected reload() {
    this.templates.reload();
    this.instances.reload();
  }

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
          await this.api.deleteTemplate(target.id, this.lifetime);
          this.notice.set('התבנית והטיוטות שלה נמחקו.');
          break;
        case 'instance':
          await this.api.deleteInstance(target.id, this.lifetime);
          this.notice.set('הטיוטה נמחקה.');
          break;
        case 'library':
          await this.api.resetLibrary(this.lifetime);
          this.notice.set('נתוני הלמידה אופסו. אפשר להתחיל עם רעיון חדש.');
          break;
      }
      if (this.lifetime.destroyed) return;
      this.confirmation().nativeElement.close();
      this.reload();
      this.heading().nativeElement.focus();
    } catch (error) {
      if (!this.lifetime.destroyed) this.deletionError.set(apiError(error));
    } finally {
      this.deleting.set(false);
    }
  }
}
