import { useId } from 'react';
import type { EditorFieldSchema, EditorSchema, EditorPageProps, ModelImage, ModelSnapshot, SchemaChoice } from './model';
import { schemaForPage } from './model';
import { editorAssetKind, editorText } from './editorLocalization';
import { ColorPickerPropertyItem } from '../../controls/ColorPickerPropertyItem';
import { ComboBoxPropertyItem } from '../../controls/ComboBoxPropertyItem';
import { DateTimeInputBox } from '../../controls/DateTimeInputBox';
import { DoubleClickValidateableTextBox } from '../../controls/DoubleClickValidateableTextBox';
import { BetterComboBox } from '../../controls/BetterComboBox';
import { BetterComboBoxPropertyItem } from '../../controls/BetterComboBoxPropertyItem';
import { InputDateTimePropertyItem } from '../../controls/InputDateTimePropertyItem';
import { InputTextPropertyItem } from '../../controls/InputTextPropertyItem';
import { NumberBox } from '../../controls/NumberBox';
import { NumericPropertyItem } from '../../controls/NumericPropertyItem';
import { ParagraphPropertyItem } from '../../controls/ParagraphPropertyItem';
import { PlanTimeWindowModePicker } from '../../controls/PlanTimeWindowModePicker';
import { PlanTimeWindowModePickerPropertyItem } from '../../controls/PlanTimeWindowModePickerPropertyItem';
import { RadioButtonInputTextPropertyItem } from '../../controls/RadioButtonInputTextPropertyItem';
import { SliderPropertyItem } from '../../controls/SliderPropertyItem';
import { SliderTextPropertyItem } from '../../controls/SliderTextPropertyItem';
import { TextPropertyItem } from '../../controls/TextPropertyItem';
import { ToggleablePropertyItem } from '../../controls/ToggleablePropertyItem';
import { TriStateTogglePropertyItem } from '../../controls/TriStateTogglePropertyItem';
import { ValidateableTextBox } from '../../controls/ValidateableTextBox';
import { ValidationErrorList } from '../../controls/ValidationErrorList';
import { isValidDateTimeText } from '../../controls/dateTimeText';
import { Labeled } from '../../controls/Labeled';
import { DescriptionGroupControl } from '../../controls/DescriptionGroupControl';
import { EmbededImageGallery, embeddedImageCollectionBytes } from '../../controls/EmbededImageGallery';
import { CollapsibleContainer } from '../../controls/CollapsibleContainer';
import { MetaDataPropertiesRepeater } from '../../controls/MetaDataPropertiesRepeater';
import { RichDescription } from '../../controls/RichDescription';
import '../../controls/propertyItems.css';
import '../../controls/descriptiveControls.css';

interface SchemaFieldRendererProps {
  schema: EditorSchema;
  snapshot?: ModelSnapshot | null;
  values?: Record<string, string>;
  images?: Record<string, ModelImage[]>;
  canEdit?: boolean;
  preview?: boolean;
  onFieldChange?: (fieldID: string, value: string) => void;
  onImagesChange?: (fieldID: string, images: ModelImage[]) => void;
  language: 'zh' | 'en';
  fieldKeyPrefix?: string;
}

const isChinese = (language: 'zh' | 'en') => language === 'zh';

export function SchemaFieldRenderer({ schema, snapshot, values, images, canEdit = false, preview = false, onFieldChange, onImagesChange, language, fieldKeyPrefix }: SchemaFieldRendererProps) {
  const handled = new Set<string>();
  const modelImageBytes = embeddedImageCollectionBytes({ ...(snapshot?.images ?? {}), ...(images ?? {}) });
  const sections = schema.groups?.length ? schema.groups : [{ title: '', fields: schema.fields.map((field) => field.id) }];
  const contents = sections.map((section) => {
    const sectionFields = section.fields.flatMap((id) => {
      const field = schema.fields.find((candidate) => candidate.id === id);
      return field ? [field] : [];
    });
    const descriptionField = sectionFields.find(isDescriptionField);
    const imageField = sectionFields.find((field) => field.kind === 'images');
    const hasDescriptionGroup = Boolean(descriptionField && imageField);
    const fields = sectionFields.map((field) => {
      handled.add(field.id);
      return <FieldControl key={field.id} schema={schema} field={field} snapshot={snapshot} values={values} images={images} modelImageBytes={modelImageBytes} canEdit={canEdit} preview={preview} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} fieldKeyPrefix={fieldKeyPrefix} richDescriptionGroup={hasDescriptionGroup} />;
    });
    if (fields.length === 0) return null;
    const repeated = <MetaDataPropertiesRepeater>{fields}</MetaDataPropertiesRepeater>;
    if (hasDescriptionGroup && descriptionField && imageField) {
      const descriptionFieldKey = fieldKeyFor(schema, descriptionField, fieldKeyPrefix);
      const imageFieldKey = fieldKeyFor(schema, imageField, fieldKeyPrefix);
      return <DescriptionGroupControl key={section.title || schema.id} title={editorText(language, section.title || 'Description Group')}
        descriptionFieldKey={descriptionFieldKey} imageFieldKey={imageFieldKey}>{repeated}</DescriptionGroupControl>;
    }
    if (!section.title) return <div key={schema.id}>{repeated}</div>;
    return <CollapsibleContainer key={section.title} title={editorText(language, section.title)}>{repeated}</CollapsibleContainer>;
  });
  const remaining = schema.fields.filter((field) => !handled.has(field.id)).map((field) => (
    <FieldControl key={field.id} schema={schema} field={field} snapshot={snapshot} values={values} images={images} modelImageBytes={modelImageBytes} canEdit={canEdit} preview={preview} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} fieldKeyPrefix={fieldKeyPrefix} />
  ));

  return <div className="editor-field-sections">{contents}{remaining.length > 0 && <MetaDataPropertiesRepeater>{remaining}</MetaDataPropertiesRepeater>}</div>;
}

function FieldControl({ schema, field, snapshot, values, images, modelImageBytes, canEdit, preview, onFieldChange, onImagesChange, language, fieldKeyPrefix, richDescriptionGroup = false }: SchemaFieldRendererProps & { field: EditorFieldSchema; schema: EditorSchema; modelImageBytes: number; richDescriptionGroup?: boolean }) {
  const generatedID = useId();
  const scopePrefix = fieldKeyPrefix ?? field.scope ?? schema.id;
  const fieldKey = fieldKeyFor(schema, field, fieldKeyPrefix);
  const value = values?.[fieldKey] ?? snapshot?.fields[fieldKey] ?? '';
  const sourceControl = (field.sourceControl ?? '').toLowerCase();
  const radioBinding = (field as EditorFieldSchema & { radioBinding?: { route: string; value: string } | null }).radioBinding;
  const mixedBy = (field as EditorFieldSchema & { mixedBy?: string | null }).mixedBy;
  const mixedValueKey = mixedBy ? `${scopePrefix}/${mixedBy}` : undefined;
  const mixedValue = mixedValueKey ? values?.[mixedValueKey] ?? snapshot?.fields[mixedValueKey] : undefined;
  const mixedStateAvailable = Boolean(mixedBy) && mixedValue !== undefined;
  const isIndeterminate = mixedStateAvailable && truthy(mixedValue);
  const isTriStateToggle = /^tristatetoggle(?:slider|propertyitem)\d*$/.test(sourceControl);
  const radioTextControl = /^radiobuttoninputtextpropertyitem\d*$/.test(sourceControl) && Boolean(radioBinding?.route && radioBinding.value);
  const title = editorText(language, snapshot?.fieldTitles?.[fieldKey] ?? field.title);
  const isReadOnly = !canEdit || preview || field.readOnly === true || snapshot?.readOnlyFields?.includes(fieldKey) === true || ['display', 'catalogFile', 'catalogTitle'].includes(field.kind) || /^textpropertyitem\d*$/.test(sourceControl);
  const visible = (field.visibleBy ?? []).every((gate) => {
    const key = `${scopePrefix}/${gate}`;
    return truthy(values?.[key] ?? snapshot?.fields[key]);
  });
  if (!visible && !preview) return null;
  const enabled = (field.enabledBy ?? []).every((gate) => {
    const key = `${scopePrefix}/${gate}`;
    const gateValue = values?.[key] ?? snapshot?.fields[key];
    return gateValue === undefined ? false : truthy(gateValue);
  });
  const disabled = isReadOnly || !enabled;
  const choices = choicesFor(field, snapshot ?? undefined, fieldKey);
  const label = <span className="editor-field-label">{title}{isReadOnly && <span className="editor-readonly-tag">{isChinese(language) ? '只读' : 'Read only'}</span>}</span>;
  const help = field.help ? <small className="editor-field-help">{editorText(language, field.help)}</small> : null;
  let control;
  if (field.kind === 'toggle') {
    control = isTriStateToggle
      ? <TriStateTogglePropertyItem id={generatedID} checked={truthy(value)} indeterminate={isIndeterminate} accessibleLabel={title} language={language} disabled={disabled}
        onChange={(checked) => onFieldChange?.(fieldKey, checked ? 'true' : 'false')} />
      : /^toggleablepropertyitem\d*$/.test(sourceControl)
        ? <ToggleablePropertyItem id={generatedID} checked={truthy(value)} title={title} disabled={disabled} onChange={(checked) => onFieldChange?.(fieldKey, checked ? 'true' : 'false')} />
        : <label className="editor-toggle"><input type="checkbox" checked={truthy(value)} disabled={disabled} onChange={(event) => onFieldChange?.(fieldKey, event.currentTarget.checked ? 'true' : 'false')} />{isChinese(language) ? '启用' : 'Enabled'}</label>;
  } else if (field.kind === 'color' || /^colorpickerpropertyitem\d*$/.test(sourceControl)) {
    control = <ColorPickerPropertyItem id={generatedID} value={value} disabled={disabled} readOnly={isReadOnly} language={language} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />;
  } else if (['choice', 'recipe', 'connection', 'classificationMapping', 'layerSource'].includes(field.kind)) {
    const hasCurrentChoice = choices.some((choice) => choice.id === value);
    const propertyComboBox = /^comboboxpropertyitem\d*$/.test(sourceControl);
    const nativeComboBox = /^combobox\d*$/.test(sourceControl);
    const betterPropertyComboBox = /^bettercomboboxpropertyitem\d*$/.test(sourceControl);
    const betterComboBox = /^bettercombobox\d*$/.test(sourceControl);
    const planModePickerPropertyItem = /^plantimewindowmodepickerpropertyitem\d*$/.test(sourceControl)
      || schema.id === 'PlanTimeWindowModePickerPropertyItem';
    const planModePicker = schema.id === 'PlanTimeWindowModePicker' && field.id === 'TimeWindowMode';
    const choiceOptions = choices.map((choice) => ({
      value: choice.id,
      label: `${choice.titleIsUserData ? choice.title : editorText(language, choice.title)}${choice.description ? ` — ${editorText(language, choice.description)}` : ''}`,
    }));
    const currentValueLabel = (currentValue: string) => isChinese(language) ? `当前值：${currentValue}` : `Current value: ${currentValue}`;
    const emptyLabel = isChinese(language) ? '请选择…' : 'Select…';
    if (planModePickerPropertyItem) {
      control = <PlanTimeWindowModePickerPropertyItem id={generatedID} timeWindowMode={value} modes={choiceOptions} disabled={disabled} currentValueLabel={currentValueLabel} emptyLabel={emptyLabel} accessibleLabel={title} onTimeWindowModeChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />;
    } else if (planModePicker) {
      control = <PlanTimeWindowModePicker id={generatedID} timeWindowMode={value} modes={choiceOptions} disabled={disabled} currentValueLabel={currentValueLabel} emptyLabel={emptyLabel} accessibleLabel={title} onTimeWindowModeChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />;
    } else if (betterPropertyComboBox) {
      control = <BetterComboBoxPropertyItem id={generatedID} selectedValue={value} itemsSource={choiceOptions} disabled={disabled} currentValueLabel={currentValueLabel} emptyLabel={emptyLabel} onSelectedValueChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />;
    } else if (betterComboBox) {
      control = <BetterComboBox id={generatedID} value={value} options={choiceOptions} disabled={disabled} currentValueLabel={currentValueLabel} emptyLabel={emptyLabel} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />;
    } else if (propertyComboBox || nativeComboBox) {
      control = <ComboBoxPropertyItem
        id={generatedID}
        value={value}
        disabled={disabled}
        currentValueLabel={currentValueLabel}
        emptyLabel={emptyLabel}
        options={choiceOptions}
        onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)}
      />;
    } else {
      control = (
        <select id={generatedID} value={value} disabled={disabled} onChange={(event) => onFieldChange?.(fieldKey, event.currentTarget.value)}>
          {!hasCurrentChoice && value !== '' && <option value={value}>{isChinese(language) ? `当前值：${value}` : `Current value: ${value}`}</option>}
          {!hasCurrentChoice && value === '' && <option value="">{isChinese(language) ? '请选择…' : 'Select…'}</option>}
          {choices.map((choice) => <option key={choice.id} value={choice.id}>{choice.titleIsUserData ? choice.title : editorText(language, choice.title)}{choice.description ? ` — ${editorText(language, choice.description)}` : ''}</option>)}
        </select>
      );
    }
  } else if (field.kind === 'images') {
    const imageList = images?.[fieldKey] ?? snapshot?.images[fieldKey] ?? [];
    control = <EmbededImageGallery fieldKey={fieldKey} images={imageList} modelImageBytes={modelImageBytes} disabled={disabled} onImagesChange={onImagesChange} language={language} />;
  } else if (field.kind === 'display') {
    control = <ParagraphPropertyItem id={generatedID} value={value} />;
  } else if (/^doubleclickvalidateabletextbox\d*$/.test(sourceControl)) {
    const error = disabled ? null : validateFieldValue(field, value, language);
    control = <>
      <DoubleClickValidateableTextBox id={generatedID} value={value} readOnly={isReadOnly} disabled={disabled} ariaInvalid={Boolean(error)} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />
      <ValidationErrorList errors={error ? [error] : []} />
    </>;
  } else if (/^validateabletextbox\d*$/.test(sourceControl)) {
    const error = disabled ? null : validateFieldValue(field, value, language);
    control = <>
      <ValidateableTextBox id={generatedID} value={value} readOnly={isReadOnly} disabled={disabled} acceptsReturn={field.kind === 'multiline'} placeholder={field.placeholder ? editorText(language, field.placeholder) : undefined} ariaInvalid={Boolean(error)} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />
      <ValidationErrorList errors={error ? [error] : []} />
    </>;
  } else if (/^textpropertyitem\d*$/.test(sourceControl)) {
    control = <TextPropertyItem value={value} language={language} />;
  } else if (radioTextControl && radioBinding) {
    const selectionKey = `${scopePrefix}/${radioBinding.route}`;
    const selectedValue = values?.[selectionKey] ?? snapshot?.fields[selectionKey] ?? '';
    const error = disabled ? null : validateFieldValue(field, value, language);
    control = <>
      <RadioButtonInputTextPropertyItem
        id={generatedID}
        value={value}
        title={title}
        groupName={selectionKey}
        optionValue={radioBinding.value}
        selectedValue={selectedValue}
        disabled={disabled}
        readOnly={isReadOnly}
        readOnlyLabel={isChinese(language) ? '只读' : 'Read only'}
        ariaInvalid={Boolean(error)}
        onTextChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)}
        onSelectionChange={(nextValue) => onFieldChange?.(selectionKey, nextValue)}
      />
      <ValidationErrorList errors={error ? [error] : []} />
    </>;
  } else if (field.kind === 'date' && (/^dateinputbox\d*$/.test(sourceControl) || /^datetimeinputbox\d*$/.test(sourceControl) || sourceControl === 'inputdatetimepropertyitem')) {
    const error = disabled ? null : validateFieldValue(field, value, language);
    control = <>
      {sourceControl === 'inputdatetimepropertyitem'
        ? <InputDateTimePropertyItem id={generatedID} mode="datetime-local" value={value} readOnly={isReadOnly} disabled={disabled} ariaInvalid={Boolean(error)} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />
        : <DateTimeInputBox id={generatedID} value={value} readOnly={isReadOnly} disabled={disabled} ariaInvalid={Boolean(error)} groupLabel={title} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />}
      <ValidationErrorList errors={error ? [error] : []} />
    </>;
  } else if (sourceControl === 'richdescription' || (field.kind === 'multiline' && richDescriptionGroup && isDescriptionField(field))) {
    const error = disabled ? null : validateFieldValue(field, value, language);
    control = <>
      <RichDescription id={generatedID} value={value} language={language} readOnly={isReadOnly} disabled={disabled}
        placeholder={field.placeholder ? editorText(language, field.placeholder) : undefined} ariaInvalid={Boolean(error)}
        onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />
      <ValidationErrorList errors={error ? [error] : []} />
    </>;
  } else if (field.kind === 'multiline') {
    control = <InputTextPropertyItem id={generatedID} value={value} readOnly={isReadOnly} disabled={disabled} acceptsReturn onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />;
  } else if (field.kind === 'slider') {
    const error = disabled ? null : validateFieldValue({ ...field, kind: 'number' }, value, language);
    if (/^slidertextpropertyitem\d*$/.test(sourceControl)) {
      control = <>
        <SliderTextPropertyItem id={generatedID} value={value} readOnly={isReadOnly} disabled={disabled} ariaInvalid={Boolean(error)} minValue={field.minimum ?? undefined} maxValue={field.maximum ?? undefined} step={field.sourceType === 'int' ? 1 : 'any'} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />
        <ValidationErrorList errors={error ? [error] : []} />
      </>;
    } else if (field.minimum != null && field.maximum != null) {
      control = <>
        <SliderPropertyItem id={generatedID} value={value} minValue={field.minimum} maxValue={field.maximum} step={field.sourceType === 'int' ? 1 : 'any'} disabled={disabled} ariaInvalid={Boolean(error)} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />
        <ValidationErrorList errors={error ? [error] : []} />
      </>;
    } else {
      control = <>
        <input id={generatedID} type="text" inputMode="decimal" value={value} readOnly={isReadOnly} disabled={disabled} placeholder={field.placeholder ? editorText(language, field.placeholder) : undefined} aria-invalid={error ? 'true' : undefined} onChange={(event) => onFieldChange?.(fieldKey, event.currentTarget.value)} />
        <ValidationErrorList errors={error ? [error] : []} />
      </>;
    }
  } else if (field.kind === 'number' && /^slidertextpropertyitem\d*$/.test(sourceControl)) {
    const error = disabled ? null : validateFieldValue(field, value, language);
    control = <>
      <SliderTextPropertyItem id={generatedID} value={value} readOnly={isReadOnly} disabled={disabled} ariaInvalid={Boolean(error)} minValue={field.minimum ?? undefined} maxValue={field.maximum ?? undefined} step={field.sourceType === 'int' ? 1 : 'any'} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />
      <ValidationErrorList errors={error ? [error] : []} />
    </>;
  } else if (field.kind === 'number' && /^numericpropertyitem\d*$/.test(sourceControl)) {
    const error = disabled ? null : validateFieldValue(field, value, language);
    control = <>
      <NumericPropertyItem id={generatedID} value={value} readOnly={isReadOnly} disabled={disabled} inputMode={field.sourceType === 'int' ? 'numeric' : 'decimal'} ariaInvalid={Boolean(error)} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />
      <ValidationErrorList errors={error ? [error] : []} />
    </>;
  } else if (field.kind === 'number' && /^numberbox\d*$/.test(sourceControl)) {
    const error = disabled ? null : validateFieldValue(field, value, language);
    control = <>
      <NumberBox id={generatedID} value={value} readOnly={isReadOnly} disabled={disabled} inputMode={field.sourceType === 'int' ? 'numeric' : 'decimal'} ariaInvalid={Boolean(error)} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />
      <ValidationErrorList errors={error ? [error] : []} />
    </>;
  } else {
    const error = disabled ? null : validateFieldValue(field, value, language);
    const inputMode = field.kind === 'number' || field.kind === 'duration' ? 'decimal' : undefined;
    const isTextField = field.kind === 'text';
    control = <>
      {isTextField
        ? <InputTextPropertyItem id={generatedID} value={value} readOnly={isReadOnly} disabled={disabled} placeholder={field.placeholder ? editorText(language, field.placeholder) : undefined} ariaInvalid={Boolean(error)} onChange={(nextValue) => onFieldChange?.(fieldKey, nextValue)} />
        : <input id={generatedID} type="text" inputMode={inputMode} value={value} readOnly={isReadOnly} disabled={disabled} placeholder={field.placeholder ? editorText(language, field.placeholder) : undefined} aria-invalid={error ? 'true' : undefined} onChange={(event) => onFieldChange?.(fieldKey, event.currentTarget.value)} />}
      <ValidationErrorList errors={error ? [error] : []} />
    </>;
  }

  return <Labeled id={generatedID} label={(field.kind !== 'toggle' && !radioTextControl) || isTriStateToggle ? label : undefined}
    labelFor={field.kind === 'images' || /^textpropertyitem\d*$/.test(sourceControl) ? null : generatedID}
    className={`editor-field editor-field-${field.kind}`} disabled={disabled} help={help}
    unavailableMessage={Boolean(field.enabledBy?.length) && !enabled && !preview
      ? <small className="editor-field-muted">{isChinese(language) ? '当前计划模式下不可用' : 'Unavailable for the current plan mode'}</small> : undefined}
    conditionalMessage={preview && Boolean(field.enabledBy?.length || field.visibleBy?.length)
      ? <small className="editor-field-muted">{isChinese(language) ? '受原始控件条件控制' : 'Controlled by source UI conditions'}</small> : undefined}>
    {control}
  </Labeled>;
}

function fieldKeyFor(schema: EditorSchema, field: EditorFieldSchema, fieldKeyPrefix?: string): string {
  return `${fieldKeyPrefix ?? field.scope ?? schema.id}/${field.id}`;
}

function isDescriptionField(field: EditorFieldSchema): boolean {
  const id = field.id.toLowerCase();
  const title = field.title.trim().toLowerCase();
  const sourceControl = (field.sourceControl ?? '').toLowerCase();
  return sourceControl === 'richdescription' || title === 'description'
    || id === 'description' || id.endsWith('/description') || id.endsWith('.description');
}

function truthy(value: string | undefined): boolean {
  return value === 'true' || value === 'True' || value === '1';
}

function choicesFor(field: EditorFieldSchema, snapshot: ModelSnapshot | undefined, fieldKey: string): SchemaChoice[] {
  return snapshot?.fieldChoices?.[fieldKey] ?? snapshot?.terrain?.fieldChoices?.[fieldKey] ?? field.choices ?? [];
}

export function validateFieldValue(field: EditorFieldSchema, value: string, language: 'zh' | 'en'): string | null {
  const invalid = isChinese(language) ? '请输入有效值。' : 'Enter a valid value.';
  if (field.kind === 'number') {
    const integer = field.sourceType === 'int';
    const pattern = integer ? /^[+-]?\d+$/ : /^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/;
    if (!pattern.test(value) || !Number.isFinite(Number(value))) return invalid;
    if (field.minimum != null && Number(value) < field.minimum) return isChinese(language) ? `值不能小于 ${field.minimum}。` : `Value must be at least ${field.minimum}.`;
    if (field.maximum != null && Number(value) > field.maximum) return isChinese(language) ? `值不能大于 ${field.maximum}。` : `Value must be at most ${field.maximum}.`;
    return null;
  }
  if (field.kind === 'duration') {
    const number = Number(value);
    if (value.trim() === '' || !Number.isFinite(number) || (field.allowZero ? number < 0 : number <= 0)) return invalid;
    if (field.minimum != null && number < field.minimum) return isChinese(language) ? `值不能小于 ${field.minimum}。` : `Value must be at least ${field.minimum}.`;
    if (field.maximum != null && number > field.maximum) return isChinese(language) ? `值不能大于 ${field.maximum}。` : `Value must be at most ${field.maximum}.`;
    return null;
  }
  if (field.kind === 'date') return value.trim() && isValidDateTimeText(value) ? null : invalid;
  if (field.kind === 'toggle') return value === 'true' || value === 'false' ? null : invalid;
  if (field.kind === 'choice' && field.choices?.length && !field.choices.some((choice) => choice.id === value)) return invalid;
  return null;
}

export function validateModelDraft(draft: EditorPageProps['draft'], schemas: EditorSchema[], snapshot: ModelSnapshot, language: 'zh' | 'en'): Array<{ fieldID: string; title: string; message: string }> {
  const errors: Array<{ fieldID: string; title: string; message: string }> = [];
  const schemaFields = schemas.flatMap((schema) => schema.fields.map((field) => ({ schema, field })));
  for (const [fieldID, value] of Object.entries(draft.fields)) {
    if (value === snapshot.fields[fieldID] || snapshot.readOnlyFields?.includes(fieldID)) continue;
    // Repeated editors add an owner prefix, but retain the schema scope. A
    // property name alone cannot identify its editor: derived channel bounds
    // share names with editable functional-surface bounds.
    const candidate = schemaFields.find(({ schema, field }) => !field.readOnly
      && (fieldID === `${field.scope ?? schema.id}/${field.id}`
        || fieldID.endsWith(`/${field.scope ?? schema.id}/${field.id}`)));
    if (!candidate) continue;
    const { field } = candidate;
    const scope = fieldID.slice(0, -(field.id.length + 1));
    if (field.enabledBy?.some((gate) => !truthy(draft.fields[`${scope}/${gate}`] ?? snapshot.fields[`${scope}/${gate}`]))) continue;
    const validationField = snapshot.fieldChoices?.[fieldID] ? { ...field, choices: snapshot.fieldChoices[fieldID] } : field;
    const error = validateFieldValue(validationField, value, language);
    if (error) errors.push({ fieldID, title: editorText(language, snapshot.fieldTitles?.[fieldID] ?? field.title), message: error });
  }
  return errors;
}

export function validateChangedTableCells(draft: EditorPageProps['draft'], snapshot: ModelSnapshot, tableSchemas: NonNullable<EditorPageProps['tableSchemas']>, language: 'zh' | 'en'): Array<{ fieldID: string; title: string; message: string }> {
  const errors: Array<{ fieldID: string; title: string; message: string }> = [];
  const baseline: Record<string, Array<{ id: string; values: Record<string, string> }>> = { ...(snapshot.terrain?.rows ?? {}) };
  for (const series of Object.values(snapshot.boundary?.series ?? {})) if (series.tableId) baseline[series.tableId] = series.rows;
  for (const input of snapshot.classification?.inputs ?? []) if (input.tableId) baseline[input.tableId] = input.rows;
  if (snapshot.manning?.rows) baseline['ClassificationLayerManningNEditor/InterpretationTable'] = snapshot.manning.rows;
  const invalid = language === 'zh' ? '请输入有效值。' : 'Enter a valid value.';
  for (const [tableID, rows] of Object.entries(draft.rows)) {
    const original = new Map((baseline[tableID] ?? []).map((row) => [row.id, row]));
    const columns = tableSchemas.find((table) => table.id === tableID)?.columns
      ?? snapshot.terrain?.tables?.[tableID]?.columns
      ?? Object.values(snapshot.boundary?.series ?? {}).find((series) => series.tableId === tableID)?.columns
      ?? [];
    for (const row of rows) {
      const originalRow = original.get(row.id);
      for (const column of columns) {
        const value = row.values[column.id] ?? '';
        if (value === (originalRow?.values[column.id] ?? '') || column.readOnly) continue;
        const sourceType = column.sourceType.toLowerCase();
        if (sourceType === 'date' || sourceType === 'datetime') {
          if (value.trim() === '' || !isValidDateTimeText(value)) errors.push({ fieldID: `${tableID}/${row.id}/${column.id}`, title: `${editorText(language, column.title)} · ${row.id}`, message: invalid });
          continue;
        }
        if (!['int', 'double', 'float', 'number'].includes(sourceType)) continue;
        const integer = sourceType === 'int';
        const pattern = integer ? /^[+-]?\d+$/ : /^[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?$/;
        if (!pattern.test(value) || !Number.isFinite(Number(value))) errors.push({ fieldID: `${tableID}/${row.id}/${column.id}`, title: `${editorText(language, column.title)} · ${row.id}`, message: invalid });
      }
    }
  }
  return errors;
}

export function SchemaPage({ language, asset, snapshot, draft, schemas, pageId, onFieldChange, onImagesChange }: EditorPageProps) {
  const schema = schemaForPage(schemas, pageId);
  if (!schema) {
    return <div className="editor-empty-state">{language === 'zh' ? '此页由后端数据视图呈现。' : 'This page is rendered from backend model data.'}</div>;
  }
  return (
    <section className="editor-schema-page" aria-label={editorText(language, schema.title)}>
      <div className="editor-schema-heading">
        <div><p className="editor-eyebrow">{editorAssetKind(language, asset.kind)}</p><h2>{editorText(language, schema.title)}</h2></div>
      </div>
      <SchemaFieldRenderer schema={schema} snapshot={snapshot} values={draft.fields} images={draft.images} canEdit={snapshot.canEdit} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} />
    </section>
  );
}

export function ConfigSchemaPreview({ schema, language }: { schema: EditorSchema; language: 'zh' | 'en' }) {
  return (
    <section className="editor-schema-page editor-config-preview">
      <div className="editor-schema-heading">
        <div><p className="editor-eyebrow">{language === 'zh' ? '原始 C# 控件定义' : 'Original C# control definition'}</p><h2>{editorText(language, schema.title)}</h2></div>
        <details className="editor-source-details"><summary>{language === 'zh' ? '源文件' : 'Source file'}</summary><code>{schema.source}</code></details>
      </div>
      <div className="editor-config-notice">{language === 'zh' ? '当前未载入模型。本页展示编辑器配置与控件条件，不会创建或保存模型数据。' : 'No model is loaded. This page shows editor metadata and source conditions; it does not create or save model data.'}</div>
      <SchemaFieldRenderer schema={schema} preview language={language} />
    </section>
  );
}
