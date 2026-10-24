import { useEffect, useRef, useState } from 'react';

export interface BetterComboBoxOption {
  value: string;
  label: string;
}

/** Controlled browser counterpart for Visual.WinUI.Controls.BetterComboBox. */
export function BetterComboBox({
  id,
  value,
  options,
  onChange,
  disabled = false,
  emptyLabel,
  currentValueLabel,
}: {
  id: string;
  value: string;
  options: BetterComboBoxOption[];
  onChange: (value: string) => void;
  disabled?: boolean;
  emptyLabel: string;
  currentValueLabel: (value: string) => string;
}) {
  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const optionRefs = useRef<Array<HTMLButtonElement | null>>([]);
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const hasCurrentChoice = options.some((option) => option.value === value);
  const displayOptions = !hasCurrentChoice && value !== ''
    ? [{ value, label: currentValueLabel(value) }, ...options]
    : options;
  const selectedOption = displayOptions.find((option) => option.value === value);
  const listID = `${id}-options`;

  useEffect(() => {
    if (!open) return;
    const closeOutside = (event: PointerEvent) => {
      if (event.target instanceof Node && !rootRef.current?.contains(event.target)) setOpen(false);
    };
    document.addEventListener('pointerdown', closeOutside);
    return () => document.removeEventListener('pointerdown', closeOutside);
  }, [open]);

  useEffect(() => {
    if (open) optionRefs.current[activeIndex]?.focus();
  }, [open, activeIndex]);

  const openAtSelection = () => {
    const selectedIndex = displayOptions.findIndex((option) => option.value === value);
    setActiveIndex(selectedIndex >= 0 ? selectedIndex : 0);
    setOpen(true);
  };
  const selectOption = (nextValue: string) => {
    onChange(nextValue);
    setOpen(false);
    triggerRef.current?.focus();
  };
  const moveActive = (offset: number) => {
    if (!displayOptions.length) return;
    setActiveIndex((current) => (current + offset + displayOptions.length) % displayOptions.length);
  };

  return (
    <div className="editor-better-combo-box" ref={rootRef}>
      <button
        ref={triggerRef}
        id={id}
        type="button"
        className="editor-better-combo-trigger"
        role="combobox"
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listID}
        aria-activedescendant={open && displayOptions[activeIndex] ? `${listID}-${activeIndex}` : undefined}
        disabled={disabled}
        onClick={() => open ? setOpen(false) : openAtSelection()}
        onKeyDown={(event) => {
          if (event.key === 'ArrowDown') {
            event.preventDefault();
            if (!open) openAtSelection();
            else moveActive(1);
          } else if (event.key === 'ArrowUp') {
            event.preventDefault();
            if (!open) openAtSelection();
            else moveActive(-1);
          } else if ((event.key === 'Enter' || event.key === ' ') && !open) {
            event.preventDefault();
            openAtSelection();
          } else if (event.key === 'Escape' && open) {
            event.preventDefault();
            setOpen(false);
          }
        }}
      >
        <span>{selectedOption?.label ?? (value === '' ? emptyLabel : currentValueLabel(value))}</span>
        <span className="editor-better-combo-caret" aria-hidden="true">▾</span>
      </button>
      {open && <ul className="editor-better-combo-options" id={listID} role="listbox" aria-labelledby={id}>
        {displayOptions.length === 0
          ? <li className="editor-better-combo-empty" role="presentation">{emptyLabel}</li>
          : displayOptions.map((option, index) => <li key={option.value} role="presentation">
            <button
              ref={(node) => { optionRefs.current[index] = node; }}
              id={`${listID}-${index}`}
              type="button"
              role="option"
              aria-selected={option.value === value}
              tabIndex={-1}
              onClick={() => selectOption(option.value)}
              onKeyDown={(event) => {
                if (event.key === 'ArrowDown') { event.preventDefault(); moveActive(1); }
                else if (event.key === 'ArrowUp') { event.preventDefault(); moveActive(-1); }
                else if (event.key === 'Home') { event.preventDefault(); setActiveIndex(0); }
                else if (event.key === 'End') { event.preventDefault(); setActiveIndex(displayOptions.length - 1); }
                else if (event.key === 'Escape') { event.preventDefault(); setOpen(false); triggerRef.current?.focus(); }
                else if (event.key === 'Tab') setOpen(false);
              }}
            >{option.label}</button>
          </li>)}
      </ul>}
    </div>
  );
}
