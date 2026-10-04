import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { FieldTree, FormField } from '@angular/forms/signals';
import { LengthForm } from '../plan-form';
import { FieldErrors } from '../../../../shared/forms/field-errors';
import { FieldValidity } from '../../../../shared/forms/field-validity';

/** The same native word-count fields serve a generated material and the generated total. */
@Component({
  imports: [FieldValidity, FieldErrors, FormField],
  selector: 'app-length-fields',
  templateUrl: './length-fields.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LengthFields {
  readonly fields = input.required<FieldTree<LengthForm>>();
  readonly prefix = input.required<string>();
  readonly label = input('אורך הטקסט');
}
