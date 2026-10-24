/** Error-message list used by ValidateableTextBox and the schema field renderer. */
export function ValidationErrorList({ errors }: { errors: readonly string[] }) {
  if (!errors.length) return null;
  return <div className="editor-field-error-list" role="alert">
    {errors.map((message, index) => <small className="editor-field-error" key={`${index}-${message}`}>{message}</small>)}
  </div>;
}
