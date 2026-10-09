import type {ReactElement, ReactNode} from 'react';
import styles from './styles.module.css';

/** Styles the ordered list it wraps as a numbered sequence of steps. */
export default function Steps({children}: {readonly children: ReactNode}): ReactElement {
  return <div className={styles.steps}>{children}</div>;
}
