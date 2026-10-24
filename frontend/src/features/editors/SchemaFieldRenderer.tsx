import { useId, useRef } from 'react';
import type { ChangeEvent } from 'react';
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
import { RadioButtonInputTextPropertyItem } from '../../controls/RadioButtonInputTextPropertyItem';
import { SliderPropertyItem } from '../../controls/SliderPropertyItem';
import { SliderTextPropertyItem } from '../../controls/SliderTextPropertyItem';
import { TextPropertyItem } from '../../controls/TextPropertyItem';
import { ToggleablePropertyItem } from '../../controls/ToggleablePropertyItem';
import { ValidateableTextBox } from '../../controls/ValidateableTextBox';
import { ValidationErrorList } from '../../controls/ValidationErrorList';
import { isValidDateTimeText } from '../../controls/dateTimeText';
import '../../controls/propertyItems.css';

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
  const sections = schema.groups?.length ? schema.groups : [{ title: '', fields: schema.fields.map((field) => field.id) }];
  const contents = sections.map((section) => {
    const fields = section.fields.flatMap((id) => {
      const field = schema.fields.find((candidate) => candidate.id === id);
      if (!field) return [];
      handled.add(id);
      return [<FieldControl key={id} schema={schema} field={field} snapshot={snapshot} values={values} images={images} canEdit={canEdit} preview={preview} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} fieldKeyPrefix={fieldKeyPrefix} />];
    });
    if (fields.length === 0) return null;
    return (
      <details className="editor-field-group" key={section.title || schema.id} open>
        {section.title && <summary>{editorText(language, section.title)}</summary>}
        <div className="editor-field-grid">{fields}</div>
      </details>
    );
  });
  const remaining = schema.fields.filter((field) => !handled.has(field.id)).map((field) => (
    <FieldControl key={field.id} schema={schema} field={field} snapshot={snapshot} values={values} images={images} canEdit={canEdit} preview={preview} onFieldChange={onFieldChange} onImagesChange={onImagesChange} language={language} fieldKeyPrefix={fieldKeyPrefix} />
  ));

  return <div className="editor-field-sections">{contents}{remaining.length > 0 && <div className="editor-field-grid">{remaining}</div>}</div>;
}

function FieldControl({ schema, field, snapshot, values, images, canEdit, preview, onFieldChange, onImagesChange, language, fieldKeyPrefix }: SchemaFieldRendererProps & { field: EditorFieldSchema; schema: EditorSchema }) {
  const generatedID = useId();
  const scopePrefix = fieldKeyPrefix ?? field.scope ?? schema.id;
  const fieldKey = `${scopePrefix}/${field.id}`;
  const value = values?.[fieldKey] ?? snapshot?.fields[fieldKey] ?? '';
  const sourceControl = (field.sourceControl ?? '').toLowerCase();
  const radioBinding = (field as EditorFieldSchema & { radioBinding?: { route: string; value: string } | null }).radioBinding;
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
    control = /^toggleablepropertyitem\d*$/.test(sourceControl)
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
    const choiceOptions = choices.map((choice) => ({
      value: choice.id,
      label: `${choice.titleIsUserData ? choice.title : editorText(language, choice.title)}${choice.description ? ` — ${editorText(language, choice.description)}` : ''}`,
    }));
    const currentValueLabel = (currentValue: string) => isChinese(language) ? `当前值：${currentValue}` : `Current value: ${currentValue}`;
    const emptyLabel = isChinese(language) ? '请选择…' : 'Select…';
    if (betterPropertyComboBox) {
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
    control = <ImageCollection fieldKey={fieldKey} images={imageList} disabled={disabled} onImagesChange={onImagesChange} language={language} />;
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

  return (
    <div className={`editor-field editor-field-${field.kind}`}>
      {field.kind !== 'toggle' && !radioTextControl && <label htmlFor={generatedID}>{label}</label>}
      {control}
      {help}
      {Boolean(field.enabledBy?.length) && !enabled && !preview && <small className="editor-field-muted">{isChinese(language) ? '当前计划模式下不可用' : 'Unavailable for the current plan mode'}</small>}
      {preview && Boolean(field.enabledBy?.length || field.visibleBy?.length) && <small className="editor-field-muted">{isChinese(language) ? '受原始控件条件控制' : 'Controlled by source UI conditions'}</small>}
    </div>
  );
}

function ImageCollection({ fieldKey, images, disabled, onImagesChange, language }: { fieldKey: string; images: ModelImage[]; disabled: boolean; onImagesChange?: (fieldID: string, images: ModelImage[]) => void; language: 'zh' | 'en' }) {
  const selectionGeneration = useRef(0);
  const onChooseFiles = async (event: ChangeEvent<HTMLInputElement>) => {
    const input = event.currentTarget;
    const generation = ++selectionGeneration.current;
    const files = Array.from(input.files ?? []);
    if (!files.length) return;
    const added = await Promise.all(files.map(async (file) => ({ id: crypto.randomUUID(), name: file.name, data: await fileAsBase64(file) })));
    if (selectionGeneration.current !== generation || !input.isConnected) return;
    onImagesChange?.(fieldKey, [...images, ...added]);
    input.value = '';
  };
  return (
    <div className="editor-image-collection">
      {images.map((image) => (
        <div className="editor-image-row" key={image.id}>
          <span title={image.name}>{image.name}</span>
          {!disabled && <button type="button" className="editor-icon-button" aria-label={isChinese(language) ? `移除 ${image.name}` : `Remove ${image.name}`} onClick={() => onImagesChange?.(fieldKey, images.filter((candidate) => candidate.id !== image.id))}>×</button>}
        </div>
      ))}
      {!disabled && <label className="editor-file-button">{isChinese(language) ? '添加图片' : 'Add image'}<input type="file" accept="image/*" multiple onChange={onChooseFiles} /></label>}
      {images.length === 0 && <small className="editor-field-muted">{isChinese(language) ? '没有嵌入图片' : 'No embedded images'}</small>}
    </div>
  );
}

function fileAsBase64(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error('Could not read image file.'));
    reader.onload = () => {
      const value = reader.result;
      if (typeof value !== 'string') return reject(new Error('Could not encode image file.'));
      resolve(value.slice(value.indexOf(',') + 1));
    };
    reader.readAsDataURL(file);
  });
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
    const candidates = schemaFields.filter(({ schema, field }) => fieldID === `${field.scope ?? schema.id}/${field.id}` || fieldID.endsWith(`/${field.id}`));
    const candidate = candidates.find(({ schema, field }) => !field.readOnly && fieldID.includes(`/${field.scope ?? schema.id}/${field.id}`))
      ?? candidates.find(({ field }) => !field.readOnly);
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
