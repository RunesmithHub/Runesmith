import {Fragment, type ReactElement} from 'react';
import clsx from 'clsx';
import styles from './styles.module.css';

const symbols: Readonly<Record<string, string>> = {
  Up: '↑',
  Down: '↓',
  Left: '←',
  Right: '→',
};

/** Splits "Ctrl+Shift+N" into keys, keeping a literal "+" key such as in "Ctrl++". */
export function splitCombo(combo: string): string[] {
  return combo.split(/\+(?=.)/).map((key) => (key === '' ? '+' : key));
}

export interface KeysProps {
  /** A combination such as "Ctrl+Shift+N". Children are used when omitted. */
  readonly combo?: string;
  readonly children?: string;
}

/** Renders a keyboard or mouse combination as individual key caps. */
export default function Keys({combo, children}: KeysProps): ReactElement {
  const keys = splitCombo(combo ?? children ?? '');
  return (
    <span className={styles.combo}>
      {keys.map((key, index) => (
        <Fragment key={index}>
          {index > 0 && <span className={styles.plus} aria-hidden="true">+</span>}
          <kbd className={clsx(styles.key, symbols[key] && styles.symbol)} aria-label={symbols[key] ? key : undefined}>
            {symbols[key] ?? key}
          </kbd>
        </Fragment>
      ))}
    </span>
  );
}
