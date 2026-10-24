/** Read-only text value counterpart for Visual.WinUI.Controls.ParagraphPropertyItem. */
export function ParagraphPropertyItem({ value, id }: { value: string; id?: string }) {
  return <output id={id} className="editor-paragraph-property-item">{value}</output>;
}
