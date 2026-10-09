import type {ReactElement, ReactNode} from 'react';
import clsx from 'clsx';
import Link from '@docusaurus/Link';
import Icon from '@site/src/components/Icon';
import type {IconName} from '@site/src/types';
import styles from './styles.module.css';

export interface CardGridProps {
  readonly columns?: 2 | 3;
  readonly children: ReactNode;
}

/** Lays out cards in a responsive grid. */
export function CardGrid({columns = 3, children}: CardGridProps): ReactElement {
  return <div className={clsx(styles.grid, columns === 2 && styles.two)}>{children}</div>;
}

export interface CardProps {
  readonly title: string;
  readonly icon?: IconName;
  readonly to?: string;
  readonly children?: ReactNode;
}

/** A feature card; the whole card becomes a link when "to" is set. */
export function Card({title, icon, to, children}: CardProps): ReactElement {
  const body = (
    <>
      {icon && (
        <span className={styles.icon}>
          <Icon name={icon} size={20} />
        </span>
      )}
      <span className={styles.title}>
        {title}
        {to && <Icon name="arrowRight" size={16} className={styles.arrow} />}
      </span>
      {children && <span className={styles.description}>{children}</span>}
    </>
  );

  return to ? (
    <Link to={to} className={clsx(styles.card, styles.link)}>
      {body}
    </Link>
  ) : (
    <div className={styles.card}>{body}</div>
  );
}
