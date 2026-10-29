import React, { forwardRef } from 'react';
import { clsx } from 'clsx';
import { twMerge } from 'tailwind-merge';

export type BadgeVariant =
  | 'neutral'
  | 'primary'
  | 'success'
  | 'warning'
  | 'error'
  | 'info'
  | 'turquoise';

export type BadgeSize = 'xs' | 'sm' | 'md';

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  variant?: BadgeVariant;
  size?: BadgeSize;
  withDot?: boolean;
  pulse?: boolean;
}

const variantStyles: Record<BadgeVariant, { container: string; dot: string }> = {
  neutral: {
    container: 'bg-studio-card text-studio-muted border-studio-border',
    dot: 'bg-studio-subtle'
  },
  primary: {
    container: 'bg-blue-500/15 text-blue-300 border-blue-500/30',
    dot: 'bg-blue-400'
  },
  success: {
    container: 'bg-emerald-500/15 text-emerald-300 border-emerald-500/30',
    dot: 'bg-emerald-400'
  },
  warning: {
    container: 'bg-amber-500/15 text-amber-300 border-amber-500/30',
    dot: 'bg-amber-400'
  },
  error: {
    container: 'bg-rose-500/15 text-rose-300 border-rose-500/30',
    dot: 'bg-rose-400'
  },
  info: {
    container: 'bg-sky-500/15 text-sky-300 border-sky-500/30',
    dot: 'bg-sky-400'
  },
  turquoise: {
    container: 'bg-cyan-500/15 text-cyan-300 border-cyan-500/30',
    dot: 'bg-cyan-400'
  }
};

const sizeStyles: Record<BadgeSize, string> = {
  xs: 'text-[10px] px-1.5 py-0.5 rounded font-mono',
  sm: 'text-xs px-2 py-0.5 rounded-md font-medium',
  md: 'text-xs px-2.5 py-1 rounded-lg font-semibold'
};

export const Badge = forwardRef<HTMLSpanElement, BadgeProps>(
  (
    {
      className,
      variant = 'neutral',
      size = 'sm',
      withDot = false,
      pulse = false,
      children,
      ...props
    },
    ref
  ) => {
    const config = variantStyles[variant];

    return (
      <span
        ref={ref}
        className={twMerge(
          clsx(
            'inline-flex items-center gap-1.5 border leading-none select-none shrink-0 transition-colors',
            config.container,
            sizeStyles[size],
            className
          )
        )}
        {...props}
      >
        {withDot && (
          <span
            className={clsx(
              'w-1.5 h-1.5 rounded-full shrink-0',
              config.dot,
              pulse && 'animate-pulse'
            )}
          />
        )}
        <span>{children}</span>
      </span>
    );
  }
);

Badge.displayName = 'Badge';

export interface StatusIndicatorProps extends React.HTMLAttributes<HTMLDivElement> {
  status: 'idle' | 'running' | 'success' | 'warning' | 'error';
  label?: string;
  size?: 'sm' | 'md';
}

export const StatusIndicator: React.FC<StatusIndicatorProps> = ({
  status,
  label,
  size = 'sm',
  className
}) => {
  const statusConfig = {
    idle: { dot: 'bg-studio-subtle', text: 'text-studio-muted' },
    running: { dot: 'bg-studio-primary animate-pulse', text: 'text-blue-300' },
    success: { dot: 'bg-studio-success', text: 'text-emerald-300' },
    warning: { dot: 'bg-studio-warning', text: 'text-amber-300' },
    error: { dot: 'bg-studio-error', text: 'text-rose-300' }
  };

  const current = statusConfig[status];

  return (
    <div className={twMerge(clsx('inline-flex items-center gap-2 select-none text-xs', className))}>
      <span
        className={clsx(
          'rounded-full shrink-0 led-indicator',
          size === 'sm' ? 'w-2 h-2' : 'w-2.5 h-2.5',
          current.dot
        )}
      />
      {label && <span className={clsx('font-medium', current.text)}>{label}</span>}
    </div>
  );
};
