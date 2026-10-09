import type {ReactElement, SVGProps} from 'react';
import {icons, type IconName} from './icons';

export interface IconProps extends Omit<SVGProps<SVGSVGElement>, 'name'> {
  readonly name: IconName;
  readonly size?: number;
}

/** Renders a named stroke icon that inherits the current text color. */
export default function Icon({name, size = 20, ...rest}: IconProps): ReactElement {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.75}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      {...rest}>
      <path d={icons[name]} />
    </svg>
  );
}
