import type { EditorSchema, EditorSchemaDocument } from './model';

const schemaURL = new URL('../../../../resources/editor-schemas.json', import.meta.url);
let cachedCatalog: Promise<{ schemas: EditorSchema[]; tables: NonNullable<EditorSchemaDocument['tables']> }> | undefined;

export function loadEditorCatalog(): Promise<{ schemas: EditorSchema[]; tables: NonNullable<EditorSchemaDocument['tables']> }> {
  cachedCatalog ??= fetch(schemaURL).then(async (response) => {
    if (!response.ok) throw new Error(`Could not load source editor metadata (${response.status}).`);
    const document = await response.json() as EditorSchemaDocument;
    const schemas = document.schemas.slice().sort((left, right) => left.title.localeCompare(right.title));
    const tables = document.tables ?? [];
    return { schemas, tables };
  });
  return cachedCatalog;
}
